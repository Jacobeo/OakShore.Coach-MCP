namespace OakShore.Coach.Domain;

public sealed record HeartRateZone(int ZoneNumber, int LowBoundaryBpm);

public sealed record SportProfile(
    string Name,
    string? TrainingMethod,
    int? RestingHeartRateBpm,
    int? MaxHeartRateBpm,
    int? LactateThresholdHeartRateBpm,
    IReadOnlyList<HeartRateZone> HeartRateZones)
{
    public const string Default = "DEFAULT";

    public bool SameBoundaries(SportProfile other) =>
        Name == other.Name && TrainingMethod == other.TrainingMethod
        && RestingHeartRateBpm == other.RestingHeartRateBpm && MaxHeartRateBpm == other.MaxHeartRateBpm
        && LactateThresholdHeartRateBpm == other.LactateThresholdHeartRateBpm
        && HeartRateZones.SequenceEqual(other.HeartRateZones);

    // Garmin configures overrides for running, cycling and swimming; every other
    // Activity type, and any sport without an override, uses the default.
    public static string Resolve(string typeKey, IReadOnlyCollection<string> configured)
    {
        string? sport =
            typeKey.Contains("running", StringComparison.Ordinal) || typeKey.EndsWith("_run", StringComparison.Ordinal) ? "RUNNING"
            : typeKey.Contains("cycling", StringComparison.Ordinal) || typeKey.Contains("biking", StringComparison.Ordinal)
                || typeKey == "virtual_ride" ? "CYCLING"
            : typeKey.Contains("swimming", StringComparison.Ordinal) ? "SWIMMING"
            : null;
        return sport is not null && configured.Contains(sport) ? sport : Default;
    }
}

public sealed record TimeInHeartRateZone(int ZoneNumber, double Seconds);

// An empty list clears the Activity's seconds; SportProfileName is then null.
public sealed record ActivityTimeInHeartRateZones(string? SportProfileName, IReadOnlyList<TimeInHeartRateZone> TimeInHeartRateZones);

public sealed record CardioDetail(
    SportProfile SportProfile,
    DateTimeOffset SportProfileObservedAt,
    IReadOnlyList<TimeInHeartRateZone> TimeInHeartRateZones);

public sealed record HeartRateZoneInput(int? ZoneNumber = null, int? LowBoundaryBpm = null);

public sealed record SportProfileInput(
    string? Name,
    string? TrainingMethod = null,
    int? RestingHeartRateBpm = null,
    int? MaxHeartRateBpm = null,
    int? LactateThresholdHeartRateBpm = null,
    IReadOnlyList<HeartRateZoneInput?>? HeartRateZones = null)
{
    public SportProfile ToSportProfile()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 50 || Name != Name.Trim())
            throw new ArgumentException("SportProfile name must be 1 to 50 nonblank characters without surrounding whitespace.");
        if (TrainingMethod is not null && (string.IsNullOrWhiteSpace(TrainingMethod) || TrainingMethod.Length > 50
            || TrainingMethod != TrainingMethod.Trim()))
            throw new ArgumentException("SportProfile trainingMethod must be 1 to 50 nonblank characters without surrounding whitespace when supplied.");
        if (RestingHeartRateBpm is < 1 or > 300 || MaxHeartRateBpm is < 1 or > 300 || LactateThresholdHeartRateBpm is < 1 or > 300)
            throw new ArgumentException("SportProfile heart rates must be between 1 and 300 bpm.");
        if (HeartRateZones is not { Count: >= 1 and <= 20 })
            throw new ArgumentException("A SportProfile carries 1 to 20 HeartRateZones.");
        var heartRateZones = HeartRateZones.Select(zone => zone is { ZoneNumber: >= 1 and <= 20, LowBoundaryBpm: >= 1 and <= 300 }
                ? new HeartRateZone(zone.ZoneNumber.Value, zone.LowBoundaryBpm.Value)
                : throw new ArgumentException("A HeartRateZone needs a zoneNumber from 1 to 20 and a lowBoundaryBpm from 1 to 300."))
            .OrderBy(zone => zone.ZoneNumber).ToArray();
        for (var index = 1; index < heartRateZones.Length; index++)
            if (heartRateZones[index].ZoneNumber == heartRateZones[index - 1].ZoneNumber
                || heartRateZones[index].LowBoundaryBpm <= heartRateZones[index - 1].LowBoundaryBpm)
                throw new ArgumentException("HeartRateZone numbers must be distinct and their lower boundaries must rise with the zone number.");
        return new SportProfile(Name, TrainingMethod, RestingHeartRateBpm, MaxHeartRateBpm, LactateThresholdHeartRateBpm, heartRateZones);
    }
}

public sealed record TimeInHeartRateZoneInput(int? ZoneNumber = null, double? Seconds = null)
{
    public static IReadOnlyList<TimeInHeartRateZone> Validate(IReadOnlyList<TimeInHeartRateZoneInput?> inputs)
    {
        if (inputs.Count > 20)
            throw new ArgumentException("An Activity carries time in at most 20 HeartRateZones.");
        var times = inputs.Select(input => input is { ZoneNumber: >= 1 and <= 20, Seconds: { } seconds }
                && double.IsFinite(seconds) && seconds is >= 0 and < 1e9
                ? new TimeInHeartRateZone(input.ZoneNumber.Value, seconds)
                : throw new ArgumentException("Time in a HeartRateZone needs a zoneNumber from 1 to 20 and finite seconds from 0 to 1000000000."))
            .OrderBy(zone => zone.ZoneNumber).ToArray();
        if (times.Select(time => time.ZoneNumber).Distinct().Count() != times.Length)
            throw new ArgumentException("An Activity's time in HeartRateZones names each HeartRateZone once.");
        return times;
    }
}
