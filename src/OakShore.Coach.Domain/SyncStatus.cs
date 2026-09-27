namespace OakShore.Coach.Domain;

public sealed record CollectionSyncStatus(string Name, int Count, DateTimeOffset? LastSyncedAt);
public sealed record SyncStatusResponse(
    IReadOnlyList<CollectionSyncStatus> Collections, Activity? LatestActivity, DateTimeOffset? LastSyncedAt);

public interface ISyncStatusRepository
{
    Task<SyncStatusResponse> GetAsync(string userId, CancellationToken cancellationToken);
}

public sealed class SyncStatusService(ISyncStatusRepository repository)
{
    public Task<SyncStatusResponse> GetAsync(string userId, CancellationToken cancellationToken) =>
        repository.GetAsync(userId, cancellationToken);
}
