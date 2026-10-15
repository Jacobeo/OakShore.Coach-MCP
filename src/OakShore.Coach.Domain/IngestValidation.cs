using System.Globalization;
using System.Text.Json;

namespace OakShore.Coach.Domain;

internal static class IngestValidation
{
    private const int MaxArchivedResponseLength = 64 * 1024;

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static DateOnly CalendarDate(string? value, DateOnly from, DateOnly to, string kind)
    {
        if (!TryParseDate(value, out var date) || date < from || date > to)
            throw new ArgumentException($"A {kind} needs a YYYY-MM-DD calendarDate within the batch's ActivityRange.");
        return date;
    }

    public static bool TryParseUtc(string? value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed);

    // Garmin's response archived verbatim, arrays refused rather than stripped so the
    // scope boundary is held at ingest as well as in the sync (ADR 0010).
    public static string? ArchivedResponse(JsonElement? response)
    {
        if (response is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element)
            return null;
        if (element.ValueKind != JsonValueKind.Object || HoldsArray(element))
            throw new ArgumentException("garminResponse must be a JSON object with no array-valued member at any depth.");
        var text = element.GetRawText();
        if (text.Length > MaxArchivedResponseLength)
            throw new ArgumentException("garminResponse must be at most 65536 characters.");
        return text;
    }

    private static bool HoldsArray(JsonElement element) =>
        element.EnumerateObject().Any(member => member.Value.ValueKind == JsonValueKind.Array
            || (member.Value.ValueKind == JsonValueKind.Object && HoldsArray(member.Value)));
}
