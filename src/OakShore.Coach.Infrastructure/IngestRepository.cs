using Microsoft.EntityFrameworkCore;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

public sealed class IngestRepository(AthleteProfileStore store, AthleteProfileRepository profiles)
    : IIngestRepository
{
    public async Task SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> profileChanges,
        ActivityRange range, IReadOnlyList<Activity> activities, string batchHash,
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
