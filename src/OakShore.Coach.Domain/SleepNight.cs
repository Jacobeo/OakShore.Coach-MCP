using System.Text.Json;

namespace OakShore.Coach.Domain;

public sealed record SleepNight(
    DateOnly CalendarDate,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    int? SleepMinutes,
    int? DeepSleepMinutes,
    int? LightSleepMinutes,
    int? RemSleepMinutes,
    int? AwakeSleepMinutes,
    int? UnmeasurableSleepMinutes,
    string? GarminResponse);

public sealed record SleepNightInput(
    string? CalendarDate,
    string? StartTimeUtc,
    string? EndTimeUtc,
    int? SleepMinutes = null,
    int? DeepSleepMinutes = null,
    int? LightSleepMinutes = null,
    int? RemSleepMinutes = null,
    int? AwakeSleepMinutes = null,
    int? UnmeasurableSleepMinutes = null,
    JsonElement? GarminResponse = null)
{
    private const int MaxMinutes = 48 * 60;

    public SleepNight ToSleepNight(DateOnly from, DateOnly to)
    {
        var calendarDate = IngestValidation.CalendarDate(CalendarDate, from, to, nameof(SleepNight));
        if (!IngestValidation.TryParseUtc(StartTimeUtc, out var start) || !IngestValidation.TryParseUtc(EndTimeUtc, out var end)
            || end < start || (end - start).TotalMinutes > MaxMinutes)
            throw new ArgumentException("A SleepNight needs startTimeUtc and endTimeUtc as UTC timestamps ending in Z, ending no earlier than it starts and within 48 hours.");
        if (new[] { SleepMinutes, DeepSleepMinutes, LightSleepMinutes, RemSleepMinutes, AwakeSleepMinutes, UnmeasurableSleepMinutes }
            .Any(minutes => minutes is < 0 or > MaxMinutes))
            throw new ArgumentException("SleepNight minutes must be between 0 and 2880.");
        return new SleepNight(calendarDate, start, end, SleepMinutes, DeepSleepMinutes, LightSleepMinutes,
            RemSleepMinutes, AwakeSleepMinutes, UnmeasurableSleepMinutes, IngestValidation.ArchivedResponse(GarminResponse));
    }
}
