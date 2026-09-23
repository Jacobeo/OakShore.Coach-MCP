namespace Coach.Domain;

public sealed record AthleteProfileChange(decimal? BodyWeightKg);

public sealed record AthleteProfileBatch(int Version, IReadOnlyList<AthleteProfileChange?>? AthleteProfiles);

public sealed class AthleteProfileIngestService(IAthleteProfileRepository repository, TimeProvider clock)
{
    public Task<AthleteProfileResponse> IngestAsync(string userId, AthleteProfileBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Version != 1)
            throw new ArgumentException("Only ingest version 1 is supported.");
        if (batch.AthleteProfiles is not { Count: > 0 and <= 100 })
            throw new ArgumentException("Supply between 1 and 100 AthleteProfile changes.");
        foreach (var change in batch.AthleteProfiles)
            AthleteProfile.ValidateBodyWeight(change?.BodyWeightKg);

        var profile = new AthleteProfile(userId, batch.AthleteProfiles[^1]!.BodyWeightKg!.Value);
        return repository.SaveAsync(profile, clock.GetUtcNow(), cancellationToken);
    }

}
