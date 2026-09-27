using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record GoalChange(string? Description, string? TargetDate);

public sealed record AthleteProfileChange(
    decimal? BodyWeightKg = null,
    IReadOnlyList<string?>? AvailableEquipment = null,
    int? IntendedTrainingFrequencyPerWeek = null,
    int? IntendedTrainingDurationMinutes = null,
    IReadOnlyList<string?>? LastingLimitations = null,
    GoalChange? Goal = null)
{
    public static void Validate(AthleteProfileChange? change, DateTimeOffset now)
    {
        if (change is null || change is { BodyWeightKg: null, AvailableEquipment: null,
            IntendedTrainingFrequencyPerWeek: null, IntendedTrainingDurationMinutes: null,
            LastingLimitations: null, Goal: null })
            throw new ArgumentException("Supply at least one AthleteProfile fact.");
        if (change.BodyWeightKg is not null)
            AthleteProfile.ValidateBodyWeight(change.BodyWeightKg);
        ValidateList(change.AvailableEquipment, "Available equipment");
        ValidateList(change.LastingLimitations, "Lasting limitations");
        if (change.IntendedTrainingFrequencyPerWeek is < 1 or > 21)
            throw new ArgumentException("Intended training frequency must be 1 to 21 times per week.");
        if (change.IntendedTrainingDurationMinutes is < 1 or > 1440)
            throw new ArgumentException("Intended training duration must be 1 to 1440 minutes.");
        if (change.Goal is not null)
        {
            if (string.IsNullOrWhiteSpace(change.Goal.Description) || change.Goal.Description.Length > 500
                || change.Goal.Description != change.Goal.Description.Trim())
                throw new ArgumentException("Goal description must be 1 to 500 nonblank characters without surrounding whitespace.");
            if (!DateOnly.TryParseExact(change.Goal.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var targetDate) || targetDate < DateOnly.FromDateTime(now.UtcDateTime))
                throw new ArgumentException("Goal target date must be today or later in YYYY-MM-DD format (UTC).");
        }
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
