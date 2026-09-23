namespace Coach.Domain;

public sealed record AthleteProfile(string UserId, decimal BodyWeightKg)
{
    public static void ValidateBodyWeight(decimal? bodyWeightKg)
    {
        if (bodyWeightKg is null or <= 0 or >= 100_000_000_000_000_000m || decimal.Round(bodyWeightKg.Value, 1) != bodyWeightKg)
            throw new ArgumentException("Body weight must be positive kilograms with at most one decimal place, below 100000000000000000.");
    }
}

public sealed record AthleteProfileResponse(AthleteProfile? Profile, DateTimeOffset? LastSyncedAt);
