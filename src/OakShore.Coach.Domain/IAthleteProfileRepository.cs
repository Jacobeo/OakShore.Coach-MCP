namespace OakShore.Coach.Domain;

public interface IAthleteProfileRepository
{
    Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken);
    Task<AthleteProfileResponse> SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> changes, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken);
}
