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
        var activityLastSyncedAt = await store.IngestBatches.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => (DateTimeOffset?)row.LastSyncedAt)
            .MaxAsync(cancellationToken);
        return new SyncStatusResponse([
            new CollectionSyncStatus("AthleteProfile", profile is null ? 0 : 1, profile),
            new CollectionSyncStatus("Activity", activityCount, activityLastSyncedAt)
        ], latestActivity, profile is null ? activityLastSyncedAt : activityLastSyncedAt is null ? profile
            : (profile > activityLastSyncedAt ? profile : activityLastSyncedAt));
    }
}
