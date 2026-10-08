using System.Text.Json;

namespace OakShore.Coach.Domain;

public sealed record SleepNight(
    DateOnly CalendarDate,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    int? SleepSeconds,
    int? DeepSleepSeconds,
    int? LightSleepSeconds,
    int? RemSleepSeconds,
    int? AwakeSleepSeconds,
    int? UnmeasurableSleepSeconds,
    string? GarminResponse);

public sealed record SleepNightInput(
    string? CalendarDate,
    string? StartTimeUtc,
    string? EndTimeUtc,
    int? SleepSeconds = null,
    int? DeepSleepSeconds = null,
    int? LightSleepSeconds = null,
    int? RemSleepSeconds = null,
    int? AwakeSleepSeconds = null,
    int? UnmeasurableSleepSeconds = null,
    JsonElement? GarminResponse = null)
{
    private const int MaxSeconds = 48 * 60 * 60;

    public SleepNight ToSleepNight(DateOnly from, DateOnly to)
    {
        var calendarDate = IngestValidation.CalendarDate(CalendarDate, from, to, nameof(SleepNight));
        if (!IngestValidation.TryParseUtc(StartTimeUtc, out var start) || !IngestValidation.TryParseUtc(EndTimeUtc, out var end)
            || end < start || (end - start).TotalSeconds > MaxSeconds)
            throw new ArgumentException("A SleepNight needs startTimeUtc and endTimeUtc as UTC timestamps ending in Z, ending no earlier than it starts and within 48 hours.");
        if (new[] { SleepSeconds, DeepSleepSeconds, LightSleepSeconds, RemSleepSeconds, AwakeSleepSeconds, UnmeasurableSleepSeconds }
            .Any(seconds => seconds is < 0 or > MaxSeconds))
            throw new ArgumentException("SleepNight seconds must be between 0 and 172800.");
        return new SleepNight(calendarDate, start, end, SleepSeconds, DeepSleepSeconds, LightSleepSeconds,
            RemSleepSeconds, AwakeSleepSeconds, UnmeasurableSleepSeconds, IngestValidation.ArchivedResponse(GarminResponse));
    }
}
