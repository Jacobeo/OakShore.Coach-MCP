using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record ActivityRange(string? FromDate, string? ToDate)
{
    public (DateOnly From, DateOnly To) Validate()
    {
        if (!DateOnly.TryParseExact(FromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(ToDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var to) || from > to)
            throw new ArgumentException("ActivityRange requires YYYY-MM-DD dates with fromDate no later than toDate.");
        return (from, to);
    }
}
