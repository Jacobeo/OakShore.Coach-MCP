using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record Goal(string Description, DateOnly TargetDate);

public sealed record AthleteProfile(
    string UserId,
    decimal? BodyWeightKg,
    IReadOnlyList<string> AvailableEquipment,
    int? IntendedTrainingFrequencyPerWeek,
    int? IntendedTrainingDurationMinutes,
    IReadOnlyList<string> LastingLimitations,
    Goal? Goal)
{
    public static AthleteProfile Empty(string userId) => new(userId, null, [], null, null, [], null);

    public AthleteProfile Apply(AthleteProfileChange change) => this with
    {
        BodyWeightKg = change.BodyWeightKg ?? BodyWeightKg,
        AvailableEquipment = change.AvailableEquipment?.Select(value => value!).ToArray() ?? AvailableEquipment,
        IntendedTrainingFrequencyPerWeek = change.IntendedTrainingFrequencyPerWeek ?? IntendedTrainingFrequencyPerWeek,
        IntendedTrainingDurationMinutes = change.IntendedTrainingDurationMinutes ?? IntendedTrainingDurationMinutes,
        LastingLimitations = change.LastingLimitations?.Select(value => value!).ToArray() ?? LastingLimitations,
        Goal = change.Goal is null ? Goal : new Goal(change.Goal.Description!,
            DateOnly.ParseExact(change.Goal.TargetDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
    };

    public static void ValidateBodyWeight(decimal? bodyWeightKg)
    {
        if (bodyWeightKg is null or <= 0 or >= 100_000_000_000_000_000m || decimal.Round(bodyWeightKg.Value, 1) != bodyWeightKg)
            throw new ArgumentException("Body weight must be positive kilograms with at most one decimal place, below 100000000000000000.");
    }
}

public sealed record AthleteProfileResponse(AthleteProfile? Profile, DateTimeOffset? LastSyncedAt);
