namespace OakShore.Coach.Domain;

public sealed class AthleteProfileService(IAthleteProfileRepository repository, IAthleteProfileIngestGateway ingest, TimeProvider clock)
{
    public Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken) =>
        repository.GetAsync(userId, cancellationToken);

    public Task<AthleteProfileResponse> UpdateAsync(AthleteProfileChange change, CancellationToken cancellationToken)
    {
        AthleteProfileChange.Validate(change, clock.GetUtcNow());
        return ingest.SendAsync(new AthleteProfileBatch(1, [change]), cancellationToken);
    }
}
