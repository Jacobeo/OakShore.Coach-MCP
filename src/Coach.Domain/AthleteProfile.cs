namespace Coach.Domain;

public sealed record AthleteProfile(string UserId, decimal BodyWeightKg);

public sealed record AthleteProfileResponse(AthleteProfile? Profile, DateTimeOffset? LastSyncedAt);
