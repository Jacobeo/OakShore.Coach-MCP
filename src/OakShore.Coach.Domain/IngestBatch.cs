using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OakShore.Coach.Domain;

public sealed record IngestBatch(
    int Version,
    IReadOnlyList<AthleteProfileChange?>? AthleteProfiles,
    ActivityRange? ActivityRange = null,
    IReadOnlyList<ActivityInput?>? Activities = null,
    IReadOnlyList<SportProfileInput?>? SportProfiles = null,
    IReadOnlyList<SleepNightInput?>? SleepNights = null,
    IReadOnlyList<DailySummaryInput?>? DailySummaries = null,
    IReadOnlyList<TrainingLoadInput?>? TrainingLoads = null,
    IReadOnlyList<VO2maxInput?>? VO2maxEstimates = null,
    bool Backfill = false);

public interface IIngestRepository
{
    Task SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> profiles,
        ActivityRange range, IReadOnlyList<Activity> activities,
        IReadOnlyDictionary<long, IReadOnlyList<ExerciseSet>> exerciseSets,
        IReadOnlyList<SportProfile> sportProfiles, IReadOnlyDictionary<long, ActivityTimeInHeartRateZones> timeInHeartRateZones,
        int version, bool backfill, IReadOnlyList<SleepNight> sleepNights, IReadOnlyList<DailySummary> dailySummaries,
        IReadOnlyList<TrainingLoad> trainingLoads, IReadOnlyList<VO2max> vo2maxEstimates,
        string batchHash, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken);
}

public sealed class IngestService(
    IAthleteProfileRepository profiles, IIngestRepository ingest, ISyncStatusRepository status, TimeProvider clock)
{
    public async Task<object> IngestAsync(string userId, IngestBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Version is not (1 or 2 or 3 or 4))
            throw new ArgumentException("Only ingest versions 1, 2, 3 and 4 are supported.");
        var now = clock.GetUtcNow();
        if (batch.Version == 1)
        {
            if (batch.ActivityRange is not null || batch.Activities is not null || batch.SportProfiles is not null
                || batch.SleepNights is not null || batch.DailySummaries is not null
                || batch.TrainingLoads is not null || batch.VO2maxEstimates is not null || batch.Backfill)
                throw new ArgumentException("Version 1 accepts AthleteProfile changes only.");
            if (batch.AthleteProfiles is not { Count: > 0 and <= 100 })
                throw new ArgumentException("Supply between 1 and 100 AthleteProfile changes.");
            foreach (var change in batch.AthleteProfiles)
                AthleteProfileChange.Validate(change, now);
            return await profiles.SaveAsync(userId, batch.AthleteProfiles.Select(change => change!).ToArray(), now, cancellationToken);
        }

        if (batch.ActivityRange is null || batch.Activities is not { Count: <= 5000 }
            || batch.AthleteProfiles is { Count: > 100 })
            throw new ArgumentException("Versions 2 to 4 require an ActivityRange and 0 to 5000 Activities, with at most 100 AthleteProfile changes.");
        // AthleteProfile changes, SportProfiles and daily form each carry a sync time of their own.
        if (batch.Backfill && (batch.Version == 4 || batch.AthleteProfiles is not null || batch.SportProfiles is not null))
            throw new ArgumentException("A backfill carries Activities, SleepNights and DailySummaries only, in version 2 or 3.");
        var (from, to) = batch.ActivityRange.Validate();
        IReadOnlyList<SleepNight> sleepNights = [];
        IReadOnlyList<DailySummary> dailySummaries = [];
        IReadOnlyList<TrainingLoad> trainingLoads = [];
        IReadOnlyList<VO2max> vo2maxEstimates = [];
        if (batch.Version == 2 && (batch.SleepNights is not null || batch.DailySummaries is not null))
            throw new ArgumentException("Version 2 carries no SleepNights or DailySummaries; version 3 does.");
        if (batch.Version < 4 && (batch.TrainingLoads is not null || batch.VO2maxEstimates is not null))
            throw new ArgumentException("Versions 2 and 3 carry no TrainingLoads or VO2max estimates; version 4 does.");
        if (batch.Version >= 3)
        {
            // Versions 3 and 4 state that every date in their range was synced, so both lists
            // are required and an empty one records a day Garmin held nothing for.
            if (batch.SleepNights is not { Count: <= 1000 } || batch.DailySummaries is not { Count: <= 1000 })
                throw new ArgumentException("Versions 3 and 4 require sleepNights and dailySummaries, each with 0 to 1000 entries.");
            sleepNights = batch.SleepNights.Select(input => (input
                ?? throw new ArgumentException("SleepNights cannot contain null.")).ToSleepNight(from, to)).ToArray();
            dailySummaries = batch.DailySummaries.Select(input => (input
                ?? throw new ArgumentException("DailySummaries cannot contain null.")).ToDailySummary(from, to)).ToArray();
            if (sleepNights.DistinctBy(night => night.CalendarDate).Count() != sleepNights.Count
                || dailySummaries.DistinctBy(summary => summary.CalendarDate).Count() != dailySummaries.Count)
                throw new ArgumentException("SleepNights and DailySummaries in one batch name each calendarDate once.");
        }
        if (batch.Version == 4)
        {
            // Version 4 states that every date in its range was synced for daily form as well.
            if (batch.TrainingLoads is not { Count: <= 1000 } || batch.VO2maxEstimates is not { Count: <= 1000 })
                throw new ArgumentException("Version 4 requires trainingLoads and vo2maxEstimates, each with 0 to 1000 entries.");
            trainingLoads = batch.TrainingLoads.Select(input => (input
                ?? throw new ArgumentException("TrainingLoads cannot contain null.")).ToTrainingLoad(from, to)).ToArray();
            vo2maxEstimates = batch.VO2maxEstimates.Select(input => (input
                ?? throw new ArgumentException("VO2max estimates cannot contain null.")).ToVO2max(to)).ToArray();
            if (trainingLoads.DistinctBy(load => load.CalendarDate).Count() != trainingLoads.Count
                || vo2maxEstimates.DistinctBy(estimate => (estimate.Sport, estimate.CalendarDate)).Count() != vo2maxEstimates.Count)
                throw new ArgumentException("TrainingLoads in one batch name each calendarDate once, and VO2max estimates each sport and calendarDate once.");
        }
        foreach (var change in batch.AthleteProfiles ?? [])
            AthleteProfileChange.Validate(change, now);
        var activities = batch.Activities.Select(input => input?.ToActivity()
            ?? throw new ArgumentException("Activities cannot contain null.")).ToArray();
        if (activities.Select(activity => activity.ActivityId).Distinct().Count() != activities.Length)
            throw new ArgumentException("Activities in one batch must have distinct ActivityIds.");
        // An Activity without the field keeps its stored ExerciseSets; an empty list clears them.
        var exerciseSets = new Dictionary<long, IReadOnlyList<ExerciseSet>>();
        foreach (var input in batch.Activities)
        {
            if (input!.ExerciseSets is not { } exerciseSetInputs)
                continue;
            if (exerciseSetInputs.Count > 1000)
                throw new ArgumentException("An Activity carries at most 1000 ExerciseSets.");
            exerciseSets[input.ActivityId] = exerciseSetInputs.Select(exerciseSetInput => (exerciseSetInput
                ?? throw new ArgumentException("ExerciseSets cannot contain null.")).ToExerciseSet()).ToArray();
        }
        if (batch.SportProfiles is { Count: > 20 })
            throw new ArgumentException("A batch carries at most 20 SportProfiles.");
        var sportProfiles = (batch.SportProfiles ?? []).Select(input => (input
            ?? throw new ArgumentException("SportProfiles cannot contain null.")).ToSportProfile()).ToArray();
        var sportProfileNames = sportProfiles.Select(profile => profile.Name).ToHashSet(StringComparer.Ordinal);
        if (sportProfileNames.Count != sportProfiles.Length)
            throw new ArgumentException("SportProfiles in one batch must have distinct names.");
        if (sportProfiles.Length > 0 && !sportProfileNames.Contains(SportProfile.Default))
            throw new ArgumentException("SportProfiles must include the DEFAULT profile.");
        // Each Activity's time in zones is measured against the SportProfile its type resolves to
        // among the boundaries delivered with it; an omitted list keeps what is stored.
        var timeInHeartRateZones = new Dictionary<long, ActivityTimeInHeartRateZones>();
        foreach (var input in batch.Activities)
        {
            if (input!.TimeInHeartRateZones is not { } timeInputs)
                continue;
            var times = TimeInHeartRateZoneInput.Validate(timeInputs);
            if (times.Count > 0 && sportProfiles.Length == 0)
                throw new ArgumentException("An Activity carrying time in HeartRateZones needs the batch's sportProfiles.");
            timeInHeartRateZones[input.ActivityId] = new ActivityTimeInHeartRateZones(
                times.Count == 0 ? null : SportProfile.Resolve(input.TypeKey!, sportProfileNames), times);
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(batch))));
        await ingest.SaveAsync(userId, (batch.AthleteProfiles ?? []).Select(change => change!).ToArray(),
            batch.ActivityRange, activities, exerciseSets, sportProfiles, timeInHeartRateZones,
            batch.Version, batch.Backfill, sleepNights, dailySummaries, trainingLoads, vo2maxEstimates, hash, now, cancellationToken);
        return await status.GetAsync(userId, cancellationToken);
    }
}
