using Microsoft.EntityFrameworkCore;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

public sealed class IngestRepository(AthleteProfileStore store, AthleteProfileRepository profiles)
    : IIngestRepository
{
    public async Task SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> profileChanges,
        ActivityRange range, IReadOnlyList<Activity> activities,
        IReadOnlyDictionary<long, IReadOnlyList<ExerciseSet>> exerciseSets, string batchHash,
        DateTimeOffset lastSyncedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await store.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (await store.IngestBatches.AnyAsync(row => row.UserId == userId && row.BatchHash == batchHash, cancellationToken))
            return;

        if (profileChanges.Count > 0)
            await profiles.ApplyChangesAsync(userId, profileChanges, lastSyncedAt, cancellationToken);
        var activityIds = activities.Select(activity => activity.ActivityId).ToArray();
        var existing = await store.Activities.Where(row => row.UserId == userId && activityIds.Contains(row.ActivityId))
            .ToDictionaryAsync(row => row.ActivityId, cancellationToken);
        foreach (var activity in activities)
        {
            if (!existing.TryGetValue(activity.ActivityId, out var row))
            {
                row = new ActivityRow { UserId = userId, ActivityId = activity.ActivityId };
                store.Activities.Add(row);
            }
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
}
