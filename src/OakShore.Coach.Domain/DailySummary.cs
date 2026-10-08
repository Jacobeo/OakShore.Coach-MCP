using System.Text.Json;

namespace OakShore.Coach.Domain;

public sealed record BodyBattery(int? Charged, int? Drained, int? Highest, int? Lowest)
{
    public bool HoldsFigures => Charged is not null || Drained is not null || Highest is not null || Lowest is not null;
}

public sealed record StressLevel(
    int? Average, int? Maximum, int? RestSeconds, int? LowSeconds, int? MediumSeconds, int? HighSeconds)
{
    public bool HoldsFigures => Average is not null || Maximum is not null || RestSeconds is not null
        || LowSeconds is not null || MediumSeconds is not null || HighSeconds is not null;
}

public sealed record DailySummary(DateOnly CalendarDate, BodyBattery BodyBattery, StressLevel StressLevel, string? GarminResponse);

public sealed record BodyBatteryInput(int? Charged = null, int? Drained = null, int? Highest = null, int? Lowest = null)
{
    public BodyBattery ToBodyBattery()
    {
        // Highest and lowest are levels on Garmin's 0-100 scale; charged and drained are
        // totals over the day, which can pass 100 when the athlete recharges more than once.
        if (Highest is < 0 or > 100 || Lowest is < 0 or > 100 || Charged is < 0 or > 1000 || Drained is < 0 or > 1000)
            throw new ArgumentException("BodyBattery highest and lowest must be between 0 and 100, and charged and drained between 0 and 1000.");
        return new BodyBattery(Charged, Drained, Highest, Lowest);
    }
}

public sealed record StressLevelInput(
    int? Average = null, int? Maximum = null, int? RestSeconds = null, int? LowSeconds = null,
    int? MediumSeconds = null, int? HighSeconds = null)
{
    // A day with a daylight saving change runs to 25 hours.
    private const int MaxSeconds = 25 * 60 * 60;

    public StressLevel ToStressLevel()
    {
        if (Average is < 0 or > 100 || Maximum is < 0 or > 100)
            throw new ArgumentException("StressLevel average and maximum must be between 0 and 100.");
        if (new[] { RestSeconds, LowSeconds, MediumSeconds, HighSeconds }.Any(seconds => seconds is < 0 or > MaxSeconds))
            throw new ArgumentException("StressLevel seconds must be between 0 and 90000.");
        return new StressLevel(Average, Maximum, RestSeconds, LowSeconds, MediumSeconds, HighSeconds);
    }
}

public sealed record DailySummaryInput(
    string? CalendarDate,
    BodyBatteryInput? BodyBattery = null,
    StressLevelInput? StressLevel = null,
    JsonElement? GarminResponse = null)
{
    public DailySummary ToDailySummary(DateOnly from, DateOnly to) => new(
        IngestValidation.CalendarDate(CalendarDate, from, to, nameof(DailySummary)),
        (BodyBattery ?? new BodyBatteryInput()).ToBodyBattery(),
        (StressLevel ?? new StressLevelInput()).ToStressLevel(),
        IngestValidation.ArchivedResponse(GarminResponse));
}
