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
        var latestActivity = await store.Activities.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.StartTimeUtc).ThenByDescending(row => row.ActivityId)
            .Select(row => new Activity(row.ActivityId, row.StartTimeUtc, row.TypeKey,
                row.DurationSeconds, row.DistanceMeters, row.TotalSets, row.ActiveSets, row.TotalReps, row.ActivityName))
            .FirstOrDefaultAsync(cancellationToken);
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
        var activityLastSyncedAt = await store.IngestBatches.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => (DateTimeOffset?)row.LastSyncedAt)
            .MaxAsync(cancellationToken);
        return new SyncStatusResponse([
            new CollectionSyncStatus("AthleteProfile", profile is null ? 0 : 1, profile),
            new CollectionSyncStatus("Activity", activityCount, activityLastSyncedAt),
            // ExerciseSets arrive only inside Activity batches, so they share that sync time:
            // a zero count with a time means completed syncs have found none.
            new CollectionSyncStatus("ExerciseSet", exerciseSetCount, activityLastSyncedAt)
        ], latestActivity, StrengthDetail.From(latestExerciseSets),
            profile is null ? activityLastSyncedAt : activityLastSyncedAt is null ? profile
            : (profile > activityLastSyncedAt ? profile : activityLastSyncedAt));
    }
}
