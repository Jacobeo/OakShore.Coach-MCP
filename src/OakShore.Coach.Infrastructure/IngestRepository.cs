using Microsoft.EntityFrameworkCore;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

public sealed class IngestRepository(AthleteProfileStore store, AthleteProfileRepository profiles)
    : IIngestRepository
{
    public async Task SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> profileChanges,
        ActivityRange range, IReadOnlyList<Activity> activities,
        IReadOnlyDictionary<long, IReadOnlyList<ExerciseSet>> exerciseSets,
        IReadOnlyList<SportProfile> sportProfiles, IReadOnlyDictionary<long, ActivityTimeInHeartRateZones> timeInHeartRateZones,
        string batchHash, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await store.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (await store.IngestBatches.AnyAsync(row => row.UserId == userId && row.BatchHash == batchHash, cancellationToken))
            return;

        if (profileChanges.Count > 0)
            await profiles.ApplyChangesAsync(userId, profileChanges, lastSyncedAt, cancellationToken);
        var activityIds = activities.Select(activity => activity.ActivityId).ToArray();
        var existing = await store.Activities.Where(row => row.UserId == userId && activityIds.Contains(row.ActivityId))
            .ToDictionaryAsync(row => row.ActivityId, cancellationToken);
        var retyped = new HashSet<long>();
        foreach (var activity in activities)
        {
            if (!existing.TryGetValue(activity.ActivityId, out var row))
            {
                row = new ActivityRow { UserId = userId, ActivityId = activity.ActivityId };
                store.Activities.Add(row);
                existing[activity.ActivityId] = row;
            }
            if (row.TypeKey != activity.TypeKey)
                retyped.Add(activity.ActivityId);
            row.StartTimeUtc = activity.StartTimeUtc;
            row.TypeKey = activity.TypeKey;
            row.DurationSeconds = activity.DurationSeconds;
            row.DistanceMeters = activity.DistanceMeters;
            row.TotalSets = activity.TotalSets;
            row.ActiveSets = activity.ActiveSets;
            row.TotalReps = activity.TotalReps;
            if (activity.ActivityName is not null)
                row.ActivityName = activity.ActivityName;
        }
        var enrichedActivityIds = exerciseSets.Keys.ToArray();
        List<ExerciseSetRow> existingExerciseSets = enrichedActivityIds.Length == 0 ? []
            : await store.ExerciseSets.Where(row => row.UserId == userId && enrichedActivityIds.Contains(row.ActivityId))
                .ToListAsync(cancellationToken);
        var exerciseSetsByActivity = existingExerciseSets.ToLookup(row => row.ActivityId);
        foreach (var (activityId, activityExerciseSets) in exerciseSets)
        {
            var rows = exerciseSetsByActivity[activityId].ToDictionary(row => row.Position);
            for (var position = 0; position < activityExerciseSets.Count; position++)
            {
                if (!rows.TryGetValue(position, out var row))
                {
                    row = new ExerciseSetRow { UserId = userId, ActivityId = activityId, Position = position };
                    store.ExerciseSets.Add(row);
                }
                var exerciseSet = activityExerciseSets[position];
                row.SetType = exerciseSet.SetType;
                row.Repetitions = exerciseSet.Repetitions;
                row.WeightKg = exerciseSet.WeightKg;
                row.Bodyweight = exerciseSet.Bodyweight;
                row.ExerciseCategory = exerciseSet.Exercise?.Category;
                row.ExerciseName = exerciseSet.Exercise?.Name;
                row.CandidateCount = exerciseSet.CandidateCount;
                row.TopProbability = exerciseSet.TopProbability;
                row.StartTimeUtc = exerciseSet.StartTimeUtc;
                row.DurationSeconds = exerciseSet.DurationSeconds;
                row.WktStepIndex = exerciseSet.WktStepIndex;
            }
            store.ExerciseSets.RemoveRange(exerciseSetsByActivity[activityId]
                .Where(row => row.Position >= activityExerciseSets.Count));
        }
        var currentSportProfiles = await SaveSportProfilesAsync(userId, sportProfiles, lastSyncedAt, cancellationToken);
        var timedActivityIds = timeInHeartRateZones.Keys.ToArray();
        var timesByActivity = (timedActivityIds.Length == 0 ? []
            : await store.TimeInHeartRateZones.Where(row => row.UserId == userId && timedActivityIds.Contains(row.ActivityId))
                .ToListAsync(cancellationToken)).ToLookup(row => row.ActivityId);
        foreach (var (activityId, activityTimes) in timeInHeartRateZones)
        {
            var activityRow = existing[activityId];
            // Pinned once, so a later boundary change never re-scores a stored Activity;
            // only a change of Activity type moves it to its new sport's SportProfile.
            if (activityTimes.SportProfileName is { } sportProfileName
                && (activityRow.SportProfileName is null || retyped.Contains(activityId)))
                (activityRow.SportProfileName, activityRow.SportProfileRevision) =
                    (sportProfileName, currentSportProfiles[sportProfileName].Revision);
            var rows = timesByActivity[activityId].ToDictionary(row => row.ZoneNumber);
            foreach (var time in activityTimes.TimeInHeartRateZones)
            {
                if (!rows.Remove(time.ZoneNumber, out var row))
                {
                    row = new TimeInHeartRateZoneRow { UserId = userId, ActivityId = activityId, ZoneNumber = time.ZoneNumber };
                    store.TimeInHeartRateZones.Add(row);
                }
                row.Seconds = time.Seconds;
            }
            store.TimeInHeartRateZones.RemoveRange(rows.Values);
        }
        var dates = range.Validate();
        store.IngestBatches.Add(new IngestBatchRow
        {
            UserId = userId,
            BatchHash = batchHash,
            FromDate = dates.From,
            ToDate = dates.To,
            LastSyncedAt = lastSyncedAt
        });
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Confirms each delivered SportProfile against its latest stored Revision, adding a
    // Revision when the boundaries differ; returns the current Revision by name.
    private async Task<Dictionary<string, SportProfileRow>> SaveSportProfilesAsync(string userId,
        IReadOnlyList<SportProfile> sportProfiles, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var current = new Dictionary<string, SportProfileRow>(StringComparer.Ordinal);
        if (sportProfiles.Count == 0)
            return current;
        var names = sportProfiles.Select(sportProfile => sportProfile.Name).ToArray();
        var latest = (await store.SportProfiles
                .Where(row => row.UserId == userId && names.Contains(row.Name)
                    && row.Revision == store.SportProfiles
                        .Where(other => other.UserId == userId && other.Name == row.Name).Max(other => other.Revision))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.Name, StringComparer.Ordinal);
        var heartRateZones = (await store.HeartRateZones
                .Where(row => row.UserId == userId && names.Contains(row.SportProfileName))
                .ToListAsync(cancellationToken))
            .ToLookup(row => (row.SportProfileName, row.SportProfileRevision));
        foreach (var sportProfile in sportProfiles)
        {
            if (latest.TryGetValue(sportProfile.Name, out var row)
                && row.ToSportProfile(heartRateZones[(row.Name, row.Revision)]).SameBoundaries(sportProfile))
            {
                row.LastObservedAt = observedAt;
                current[sportProfile.Name] = row;
                continue;
            }
            var revision = new SportProfileRow
            {
                UserId = userId,
                Name = sportProfile.Name,
                Revision = (row?.Revision ?? 0) + 1,
                TrainingMethod = sportProfile.TrainingMethod,
                RestingHeartRateBpm = sportProfile.RestingHeartRateBpm,
                MaxHeartRateBpm = sportProfile.MaxHeartRateBpm,
                LactateThresholdHeartRateBpm = sportProfile.LactateThresholdHeartRateBpm,
                FirstObservedAt = observedAt,
                LastObservedAt = observedAt
            };
            store.SportProfiles.Add(revision);
            store.HeartRateZones.AddRange(sportProfile.HeartRateZones.Select(zone => new HeartRateZoneRow
            {
                UserId = userId,
                SportProfileName = sportProfile.Name,
                SportProfileRevision = revision.Revision,
                ZoneNumber = zone.ZoneNumber,
                LowBoundaryBpm = zone.LowBoundaryBpm
            }));
            current[sportProfile.Name] = revision;
        }
        return current;
    }
}
