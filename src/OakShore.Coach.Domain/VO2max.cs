namespace OakShore.Coach.Domain;

// Dated by the day Garmin computed it rather than the day it was read, so it may precede
// the batch's range but never follow it (ADR 0012).
public sealed record VO2max(string Sport, DateOnly CalendarDate, double MlPerKgMin);

public sealed record VO2maxInput(string? Sport, string? CalendarDate, double? MlPerKgMin = null)
{
    private static readonly HashSet<string> Sports = new(StringComparer.Ordinal) { "generic", "cycling" };

    public VO2max ToVO2max(DateOnly to)
    {
        if (Sport is null || !Sports.Contains(Sport))
            throw new ArgumentException("A VO2max estimate's sport is generic or cycling.");
        if (!IngestValidation.TryParseDate(CalendarDate, out var calendarDate) || calendarDate > to)
            throw new ArgumentException("A VO2max estimate needs a YYYY-MM-DD calendarDate no later than the batch's ActivityRange ends.");
        // An absent estimate is left out, never sent as zero.
        if (MlPerKgMin is not (> 0 and <= 100))
            throw new ArgumentException("A VO2max estimate's mlPerKgMin must be greater than 0 and at most 100.");
        return new VO2max(Sport, calendarDate, MlPerKgMin.Value);
    }
}
