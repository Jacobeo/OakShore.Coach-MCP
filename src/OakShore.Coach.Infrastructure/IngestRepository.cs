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
        int version, IReadOnlyList<SleepNight> sleepNights, IReadOnlyList<DailySummary> dailySummaries,
        IReadOnlyList<TrainingLoad> trainingLoads, IReadOnlyList<VO2max> vo2maxEstimates,
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
        await SaveSleepNightsAsync(userId, sleepNights, cancellationToken);
        await SaveDailySummariesAsync(userId, dailySummaries, cancellationToken);
        await SaveTrainingLoadsAsync(userId, trainingLoads, cancellationToken);
        await SaveVO2maxEstimatesAsync(userId, vo2maxEstimates, cancellationToken);
        var dates = range.Validate();
        store.IngestBatches.Add(new IngestBatchRow
        {
            UserId = userId,
            BatchHash = batchHash,
            FromDate = dates.From,
            ToDate = dates.To,
            LastSyncedAt = lastSyncedAt,
            Version = version
        });
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // A delivered day replaces its stored figures; a day a batch leaves out keeps them, as an
    // Activity left out of a later batch is kept. An omitted garminResponse keeps the archived one.
    private async Task SaveSleepNightsAsync(string userId, IReadOnlyList<SleepNight> sleepNights, CancellationToken cancellationToken)
    {
        if (sleepNights.Count == 0)
            return;
        var calendarDates = sleepNights.Select(night => night.CalendarDate).ToArray();
        var stored = await store.SleepNights.Where(row => row.UserId == userId && calendarDates.Contains(row.CalendarDate))
            .ToDictionaryAsync(row => row.CalendarDate, cancellationToken);
        foreach (var night in sleepNights)
        {
            if (!stored.TryGetValue(night.CalendarDate, out var row))
            {
                row = new SleepNightRow { UserId = userId, CalendarDate = night.CalendarDate };
                store.SleepNights.Add(row);
            }
            row.StartTimeUtc = night.StartTimeUtc;
            row.EndTimeUtc = night.EndTimeUtc;
            row.SleepMinutes = night.SleepMinutes;
            row.DeepSleepMinutes = night.DeepSleepMinutes;
            row.LightSleepMinutes = night.LightSleepMinutes;
            row.RemSleepMinutes = night.RemSleepMinutes;
            row.AwakeSleepMinutes = night.AwakeSleepMinutes;
            row.UnmeasurableSleepMinutes = night.UnmeasurableSleepMinutes;
            if (night.GarminResponse is not null)
                row.GarminResponse = night.GarminResponse;
        }
    }

    private async Task SaveDailySummariesAsync(string userId, IReadOnlyList<DailySummary> dailySummaries, CancellationToken cancellationToken)
    {
        if (dailySummaries.Count == 0)
            return;
        var calendarDates = dailySummaries.Select(summary => summary.CalendarDate).ToArray();
        var stored = await store.DailySummaries.Where(row => row.UserId == userId && calendarDates.Contains(row.CalendarDate))
            .ToDictionaryAsync(row => row.CalendarDate, cancellationToken);
        foreach (var summary in dailySummaries)
        {
            if (!stored.TryGetValue(summary.CalendarDate, out var row))
            {
                row = new DailySummaryRow { UserId = userId, CalendarDate = summary.CalendarDate };
                store.DailySummaries.Add(row);
            }
            row.BodyBatteryCharged = summary.BodyBattery.Charged;
            row.BodyBatteryDrained = summary.BodyBattery.Drained;
            row.BodyBatteryHighest = summary.BodyBattery.Highest;
            row.BodyBatteryLowest = summary.BodyBattery.Lowest;
            row.StressLevelAverage = summary.StressLevel.Average;
            row.StressLevelMaximum = summary.StressLevel.Maximum;
            row.StressLevelRestSeconds = summary.StressLevel.RestSeconds;
            row.StressLevelLowSeconds = summary.StressLevel.LowSeconds;
            row.StressLevelMediumSeconds = summary.StressLevel.MediumSeconds;
            row.StressLevelHighSeconds = summary.StressLevel.HighSeconds;
            if (summary.GarminResponse is not null)
                row.GarminResponse = summary.GarminResponse;
        }
    }

    private async Task SaveTrainingLoadsAsync(string userId, IReadOnlyList<TrainingLoad> trainingLoads, CancellationToken cancellationToken)
    {
        if (trainingLoads.Count == 0)
            return;
        var calendarDates = trainingLoads.Select(load => load.CalendarDate).ToArray();
        var stored = await store.TrainingLoads.Where(row => row.UserId == userId && calendarDates.Contains(row.CalendarDate))
            .ToDictionaryAsync(row => row.CalendarDate, cancellationToken);
        foreach (var load in trainingLoads)
        {
            if (!stored.TryGetValue(load.CalendarDate, out var row))
            {
                row = new TrainingLoadRow { UserId = userId, CalendarDate = load.CalendarDate };
                store.TrainingLoads.Add(row);
            }
            row.WeeklyTrainingLoad = load.WeeklyTrainingLoad;
            row.LoadTunnelMin = load.LoadTunnelMin;
            row.LoadTunnelMax = load.LoadTunnelMax;
            if (load.GarminResponse is not null)
                row.GarminResponse = load.GarminResponse;
        }
    }

    private async Task SaveVO2maxEstimatesAsync(string userId, IReadOnlyList<VO2max> vo2maxEstimates, CancellationToken cancellationToken)
    {
        if (vo2maxEstimates.Count == 0)
            return;
        var calendarDates = vo2maxEstimates.Select(estimate => estimate.CalendarDate).Distinct().ToArray();
        var stored = await store.VO2maxEstimates.Where(row => row.UserId == userId && calendarDates.Contains(row.CalendarDate))
            .ToDictionaryAsync(row => (row.Sport, row.CalendarDate), cancellationToken);
        foreach (var estimate in vo2maxEstimates)
        {
            if (!stored.TryGetValue((estimate.Sport, estimate.CalendarDate), out var row))
            {
                row = new VO2maxEstimateRow { UserId = userId, Sport = estimate.Sport, CalendarDate = estimate.CalendarDate };
                store.VO2maxEstimates.Add(row);
            }
            row.MlPerKgMin = estimate.MlPerKgMin;
        }
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
