namespace OakShore.Coach.Domain;

public sealed record CollectionSyncStatus(string Name, int Count, DateTimeOffset? LastSyncedAt);
public sealed record SyncStatusResponse(
    IReadOnlyList<CollectionSyncStatus> Collections, Activity? LatestActivity,
    StrengthDetail? LatestActivityStrengthDetail, CardioDetail? LatestActivityCardioDetail,
    LatestWellnessDay? LatestWellnessDay, DateTimeOffset? LastSyncedAt);

// The most recent date a version 3 or later batch synced, and which kinds of record it holds;
// an empty list is a day Garmin held nothing for, not a day never attempted. Never values.
public sealed record LatestWellnessDay(DateOnly CalendarDate, IReadOnlyList<string> Holds)
{
    public static LatestWellnessDay From(DateOnly calendarDate, bool holdsSleepNight, DailySummary? dailySummary,
        bool holdsTrainingLoad)
    {
        var holds = new List<string>();
        if (holdsSleepNight)
            holds.Add(nameof(SleepNight));
        if (dailySummary is not null)
        {
            holds.Add(nameof(DailySummary));
            if (dailySummary.BodyBattery.HoldsFigures)
                holds.Add(nameof(BodyBattery));
            if (dailySummary.StressLevel.HoldsFigures)
                holds.Add(nameof(StressLevel));
        }
        if (holdsTrainingLoad)
            holds.Add(nameof(TrainingLoad));
        return new LatestWellnessDay(calendarDate, holds);
    }
}

public interface ISyncStatusRepository
{
    Task<SyncStatusResponse> GetAsync(string userId, CancellationToken cancellationToken);
}

public sealed class SyncStatusService(ISyncStatusRepository repository)
{
    public Task<SyncStatusResponse> GetAsync(string userId, CancellationToken cancellationToken) =>
        repository.GetAsync(userId, cancellationToken);
}
