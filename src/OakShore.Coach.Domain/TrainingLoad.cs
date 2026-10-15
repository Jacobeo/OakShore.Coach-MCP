using System.Text.Json;

namespace OakShore.Coach.Domain;

// The systemic axis only, stored as Garmin sent it: nothing recomputed (ADR 0009).
public sealed record TrainingLoad(
    DateOnly CalendarDate, int? WeeklyTrainingLoad, int? LoadTunnelMin, int? LoadTunnelMax, string? GarminResponse);

public sealed record TrainingLoadInput(
    string? CalendarDate,
    int? WeeklyTrainingLoad = null,
    int? LoadTunnelMin = null,
    int? LoadTunnelMax = null,
    JsonElement? GarminResponse = null)
{
    private const int MaxLoad = 100_000;

    public TrainingLoad ToTrainingLoad(DateOnly from, DateOnly to)
    {
        var calendarDate = IngestValidation.CalendarDate(CalendarDate, from, to, nameof(TrainingLoad));
        if (new[] { WeeklyTrainingLoad, LoadTunnelMin, LoadTunnelMax }.Any(load => load is < 0 or > MaxLoad))
            throw new ArgumentException("TrainingLoad weeklyTrainingLoad, loadTunnelMin and loadTunnelMax must be between 0 and 100000.");
        return new TrainingLoad(calendarDate, WeeklyTrainingLoad, LoadTunnelMin, LoadTunnelMax,
            IngestValidation.ArchivedResponse(GarminResponse));
    }
}
