using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record Activity(
    long ActivityId,
    DateTimeOffset StartTimeUtc,
    string TypeKey,
    double DurationSeconds,
    double? DistanceMeters,
    int? TotalSets,
    int? ActiveSets,
    int? TotalReps,
    string? ActivityName);

public sealed record ActivityInput(
    long ActivityId,
    string? StartTimeUtc,
    string? TypeKey,
    double? DurationSeconds,
    double? DistanceMeters = null,
    int? TotalSets = null,
    int? ActiveSets = null,
    int? TotalReps = null,
    string? ActivityName = null,
    IReadOnlyList<ExerciseSetInput?>? ExerciseSets = null,
    IReadOnlyList<TimeInHeartRateZoneInput?>? TimeInHeartRateZones = null)
{
    public Activity ToActivity()
    {
        if (ActivityId <= 0)
            throw new ArgumentException("ActivityId must be positive.");
        if (!DateTimeOffset.TryParseExact(StartTimeUtc,
                ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
            throw new ArgumentException("Activity startTimeUtc must be a UTC timestamp ending in Z.");
        if (string.IsNullOrWhiteSpace(TypeKey) || TypeKey.Length > 100 || TypeKey != TypeKey.Trim())
            throw new ArgumentException("Activity typeKey must be 1 to 100 nonblank characters without surrounding whitespace.");
        if (DurationSeconds is not { } duration || !double.IsFinite(duration) || duration < 0 || duration >= 1e9)
            throw new ArgumentException("Activity durationSeconds must be finite and between 0 and 1000000000.");
        if (DistanceMeters is { } distance && (!double.IsFinite(distance) || distance < 0 || distance >= 1e9))
            throw new ArgumentException("Activity distanceMeters must be finite and between 0 and 1000000000.");
        if (TotalSets < 0 || ActiveSets < 0 || TotalReps < 0 || ActiveSets > TotalSets)
            throw new ArgumentException("Activity strength totals must be nonnegative and activeSets cannot exceed totalSets.");
        if (ActivityName is not null && (ActivityName.Length > 500 || string.IsNullOrWhiteSpace(ActivityName)))
            throw new ArgumentException("Activity activityName must be 1 to 500 nonblank characters when supplied.");
        return new Activity(ActivityId, start, TypeKey, duration, DistanceMeters, TotalSets, ActiveSets, TotalReps, ActivityName);
    }
}
