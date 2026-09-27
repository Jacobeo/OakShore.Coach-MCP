using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record GoalChange(string? Description, string? TargetDate);
public sealed record GoalInput(string? Description);
public sealed record ConstraintInput(string? Description, string? ValidFrom, string? ValidUntil)
{
    private static readonly string[] UtcFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    public Constraint ToConstraint()
    {
        if (string.IsNullOrWhiteSpace(Description) || Description.Length > 500 || Description != Description.Trim())
            throw new ArgumentException("Constraint description must be 1 to 500 nonblank characters without surrounding whitespace.");
        if (!TryUtc(ValidFrom, out var from) || !TryUtc(ValidUntil, out var until) || from >= until)
            throw new ArgumentException("Constraint validity requires UTC timestamps with Z in YYYY-MM-DDTHH:MM:SSZ format and validFrom earlier than validUntil; validFrom is inclusive and validUntil is exclusive.");
        return new Constraint(Description, from, until);
    }

    private static bool TryUtc(string? value, out DateTimeOffset instant)
    {
        if (DateTime.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            instant = new DateTimeOffset(parsed);
            return true;
        }
        instant = default;
        return false;
    }
}

public sealed record AthleteProfileChange(
    decimal? BodyWeightKg = null,
    IReadOnlyList<string?>? AvailableEquipment = null,
    int? IntendedTrainingFrequencyPerWeek = null,
    int? IntendedTrainingDurationMinutes = null,
    IReadOnlyList<string?>? LastingLimitations = null,
    IReadOnlyList<GoalInput?>? Goals = null,
    string? GoalsTargetDate = null,
    GoalChange? Goal = null,
    ConstraintInput? Constraint = null)
{
    public static void Validate(AthleteProfileChange? change, DateTimeOffset now)
    {
        if (change is null || change is { BodyWeightKg: null, AvailableEquipment: null,
            IntendedTrainingFrequencyPerWeek: null, IntendedTrainingDurationMinutes: null,
            LastingLimitations: null, Goals: null, GoalsTargetDate: null, Goal: null, Constraint: null })
            throw new ArgumentException("Supply at least one AthleteProfile fact.");
        if (change.BodyWeightKg is not null)
            AthleteProfile.ValidateBodyWeight(change.BodyWeightKg);
        ValidateList(change.AvailableEquipment, "Available equipment");
        ValidateList(change.LastingLimitations, "Lasting limitations");
        if (change.IntendedTrainingFrequencyPerWeek is < 1 or > 21)
            throw new ArgumentException("Intended training frequency must be 1 to 21 times per week.");
        if (change.IntendedTrainingDurationMinutes is < 1 or > 1440)
            throw new ArgumentException("Intended training duration must be 1 to 1440 minutes.");
        if (change.Goal is not null && (change.Goals is not null || change.GoalsTargetDate is not null))
            throw new ArgumentException("Supply either goal or goals and goalsTargetDate, not both.");
        if (change.Goals is not null)
        {
            if (change.Goals.Count > 20 || change.Goals.Any(goal => !ValidDescription(goal?.Description))
                || change.Goals.Select(goal => goal!.Description).Distinct(StringComparer.OrdinalIgnoreCase).Count() != change.Goals.Count)
                throw new ArgumentException("Goals must contain at most 20 distinct descriptions of 1 to 500 nonblank characters, ordered highest priority first.");
            if (change.Goals.Count == 0 && change.GoalsTargetDate is not null)
                throw new ArgumentException("An empty goals list clears the shared date; do not supply goalsTargetDate with it.");
        }
        if (change.GoalsTargetDate is not null)
            ValidateDate(change.GoalsTargetDate, now);
        if (change.Goal is not null)
        {
            if (!ValidDescription(change.Goal.Description))
                throw new ArgumentException("Goal description must be 1 to 500 nonblank characters without surrounding whitespace.");
            ValidateDate(change.Goal.TargetDate, now);
        }
        change.Constraint?.ToConstraint();
    }

    private static bool ValidDescription(string? description) =>
        !string.IsNullOrWhiteSpace(description) && description.Length <= 500 && description == description.Trim();

    private static void ValidateDate(string? value, DateTimeOffset now)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var targetDate) || targetDate < DateOnly.FromDateTime(now.UtcDateTime))
            throw new ArgumentException("Shared Goals target date must be today or later in YYYY-MM-DD format (UTC).");
    }

    private static void ValidateList(IReadOnlyList<string?>? values, string name)
    {
        if (values is null)
            return;
        if (values.Count > 50 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200 || value != value.Trim()))
            throw new ArgumentException($"{name} must contain at most 50 entries of 1 to 200 nonblank characters without surrounding whitespace.");
    }
}

public sealed record AthleteProfileBatch(int Version, IReadOnlyList<AthleteProfileChange?>? AthleteProfiles);

public sealed class AthleteProfileIngestService(IAthleteProfileRepository repository, TimeProvider clock)
{
    public Task<AthleteProfileResponse> IngestAsync(string userId, AthleteProfileBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Version != 1)
            throw new ArgumentException("Only ingest version 1 is supported.");
        if (batch.AthleteProfiles is not { Count: > 0 and <= 100 })
            throw new ArgumentException("Supply between 1 and 100 AthleteProfile changes.");
        var now = clock.GetUtcNow();
        foreach (var change in batch.AthleteProfiles)
            AthleteProfileChange.Validate(change, now);

        return repository.SaveAsync(userId, batch.AthleteProfiles.Select(change => change!).ToArray(), now, cancellationToken);
    }

}
