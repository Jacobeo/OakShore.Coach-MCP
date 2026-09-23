namespace Coach.Domain;

public sealed class AthleteProfileService(IAthleteProfileRepository repository, IAthleteProfileIngestGateway ingest)
{
    public Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken) =>
        repository.GetAsync(userId, cancellationToken);

    public Task<AthleteProfileResponse> UpdateAsync(decimal bodyWeightKg, CancellationToken cancellationToken)
    {
        AthleteProfile.ValidateBodyWeight(bodyWeightKg);
        return ingest.SendAsync(new AthleteProfileBatch(1, [new AthleteProfileChange(bodyWeightKg)]), cancellationToken);
    }
}
