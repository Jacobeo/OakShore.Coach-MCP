using Microsoft.EntityFrameworkCore;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

public sealed class SyncStatusRepository(AthleteProfileStore store) : ISyncStatusRepository
{
    public async Task<SyncStatusResponse> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await store.AthleteProfiles.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => (DateTimeOffset?)row.LastSyncedAt)
            .SingleOrDefaultAsync(cancellationToken);
        var activityCount = await store.Activities.AsNoTracking()
            .CountAsync(row => row.UserId == userId, cancellationToken);
        var latest = await store.Activities.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.StartTimeUtc).ThenByDescending(row => row.ActivityId)
            .Select(row => new
            {
                Activity = new Activity(row.ActivityId, row.StartTimeUtc, row.TypeKey, row.DurationSeconds,
                    row.DistanceMeters, row.TotalSets, row.ActiveSets, row.TotalReps, row.ActivityName),
                row.SportProfileName,
                row.SportProfileRevision
            })
            .FirstOrDefaultAsync(cancellationToken);
        var latestActivity = latest?.Activity;
        var exerciseSetCount = await store.ExerciseSets.AsNoTracking()
            .CountAsync(row => row.UserId == userId, cancellationToken);
        IReadOnlyList<ExerciseSet> latestExerciseSets = latestActivity is null ? []
            : await store.ExerciseSets.AsNoTracking()
                .Where(row => row.UserId == userId && row.ActivityId == latestActivity.ActivityId)
                .OrderBy(row => row.Position)
                .Select(row => new ExerciseSet(row.SetType, row.Repetitions, row.WeightKg, row.Bodyweight,
                    row.ExerciseCategory == null ? null : new Exercise(row.ExerciseCategory, row.ExerciseName),
                    row.CandidateCount, row.TopProbability, row.StartTimeUtc, row.DurationSeconds, row.WktStepIndex))
                .ToArrayAsync(cancellationToken);
        CardioDetail? latestCardioDetail = null;
        IReadOnlyList<TimeInHeartRateZone> latestTimes = latest is null ? []
            : await store.TimeInHeartRateZones.AsNoTracking()
                .Where(row => row.UserId == userId && row.ActivityId == latest.Activity.ActivityId)
                .OrderBy(row => row.ZoneNumber)
                .Select(row => new TimeInHeartRateZone(row.ZoneNumber, row.Seconds))
                .ToArrayAsync(cancellationToken);
        // Cleared seconds keep their revision for a later re-send, but there is nothing to report.
        if (latestTimes.Count > 0 && latest is { SportProfileName: { } sportProfileName, SportProfileRevision: { } revision })
        {
            var sportProfileRow = await store.SportProfiles.AsNoTracking()
                .SingleAsync(row => row.UserId == userId && row.Name == sportProfileName && row.Revision == revision, cancellationToken);
            var heartRateZones = await store.HeartRateZones.AsNoTracking()
                .Where(row => row.UserId == userId && row.SportProfileName == sportProfileName && row.SportProfileRevision == revision)
                .ToListAsync(cancellationToken);
            latestCardioDetail = new CardioDetail(sportProfileRow.ToSportProfile(heartRateZones),
                sportProfileRow.FirstObservedAt, latestTimes);
        }
        var sportProfileCount = await store.SportProfiles.AsNoTracking()
            .Where(row => row.UserId == userId).Select(row => row.Name).Distinct().CountAsync(cancellationToken);
        var sportProfileLastSyncedAt = await store.SportProfiles.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => (DateTimeOffset?)row.LastObservedAt)
            .MaxAsync(cancellationToken);
        var activityLastSyncedAt = await store.IngestBatches.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => (DateTimeOffset?)row.LastSyncedAt)
            .MaxAsync(cancellationToken);
        return new SyncStatusResponse([
            new CollectionSyncStatus("AthleteProfile", profile is null ? 0 : 1, profile),
            new CollectionSyncStatus("Activity", activityCount, activityLastSyncedAt),
            // ExerciseSets arrive only inside Activity batches, so they share that sync time:
            // a zero count with a time means completed syncs have found none.
            new CollectionSyncStatus("ExerciseSet", exerciseSetCount, activityLastSyncedAt),
            // Boundaries arrive only with Activities that carry time in zones, so they keep
            // their own time: the last batch that delivered them.
            new CollectionSyncStatus("SportProfile", sportProfileCount, sportProfileLastSyncedAt)
        ], latestActivity, StrengthDetail.From(latestExerciseSets), latestCardioDetail,
            profile is null ? activityLastSyncedAt : activityLastSyncedAt is null ? profile
            : (profile > activityLastSyncedAt ? profile : activityLastSyncedAt));
    }
}
