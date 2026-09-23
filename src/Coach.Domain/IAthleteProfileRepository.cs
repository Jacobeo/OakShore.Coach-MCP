namespace Coach.Domain;

public interface IAthleteProfileRepository
{
    Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken);
    Task<AthleteProfileResponse> SaveAsync(AthleteProfile profile, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken);
}
