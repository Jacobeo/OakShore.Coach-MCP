using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record Goal(string Description);
public sealed record Constraint(string Description, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil);

public sealed record AthleteProfile(
    string UserId,
    decimal? BodyWeightKg,
    IReadOnlyList<string> AvailableEquipment,
    int? IntendedTrainingFrequencyPerWeek,
    int? IntendedTrainingDurationMinutes,
    IReadOnlyList<string> LastingLimitations,
    IReadOnlyList<Goal> Goals,
    DateOnly? GoalsTargetDate,
    IReadOnlyList<Constraint> Constraints)
{
    public static AthleteProfile Empty(string userId) => new(userId, null, [], null, null, [], [], null, []);

    public AthleteProfile Apply(AthleteProfileChange change)
    {
        var goals = change.Goals is not null
            ? change.Goals.Select(goal => new Goal(goal!.Description!)).ToArray()
            : change.Goal is not null ? [new Goal(change.Goal.Description!)] : Goals;
        var targetDate = change.Goal is not null
            ? DateOnly.ParseExact(change.Goal.TargetDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : change.Goals is { Count: 0 } ? null
            : change.GoalsTargetDate is not null
                ? DateOnly.ParseExact(change.GoalsTargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : GoalsTargetDate;
        return this with
        {
            BodyWeightKg = change.BodyWeightKg ?? BodyWeightKg,
            AvailableEquipment = change.AvailableEquipment?.Select(value => value!).ToArray() ?? AvailableEquipment,
            IntendedTrainingFrequencyPerWeek = change.IntendedTrainingFrequencyPerWeek ?? IntendedTrainingFrequencyPerWeek,
            IntendedTrainingDurationMinutes = change.IntendedTrainingDurationMinutes ?? IntendedTrainingDurationMinutes,
            LastingLimitations = change.LastingLimitations?.Select(value => value!).ToArray() ?? LastingLimitations,
            Goals = goals,
            GoalsTargetDate = targetDate
        };
    }

    public static void ValidateBodyWeight(decimal? bodyWeightKg)
    {
        if (bodyWeightKg is null or <= 0 or >= 100_000_000_000_000_000m || decimal.Round(bodyWeightKg.Value, 1) != bodyWeightKg)
            throw new ArgumentException("Body weight must be positive kilograms with at most one decimal place, below 100000000000000000.");
    }
}

public sealed record AthleteProfileResponse(
    AthleteProfile? Profile, DateTimeOffset? LastSyncedAt, IReadOnlyList<Constraint>? SavedConstraints = null);
