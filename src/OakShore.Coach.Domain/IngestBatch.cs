using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OakShore.Coach.Domain;

public sealed record IngestBatch(
    int Version,
    IReadOnlyList<AthleteProfileChange?>? AthleteProfiles,
    ActivityRange? ActivityRange = null,
    IReadOnlyList<ActivityInput?>? Activities = null);

public interface IIngestRepository
{
    Task SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> profiles,
        ActivityRange range, IReadOnlyList<Activity> activities,
        IReadOnlyDictionary<long, IReadOnlyList<ExerciseSet>> exerciseSets, string batchHash,
        DateTimeOffset lastSyncedAt, CancellationToken cancellationToken);
}

public sealed class IngestService(
    IAthleteProfileRepository profiles, IIngestRepository ingest, ISyncStatusRepository status, TimeProvider clock)
{
    public async Task<object> IngestAsync(string userId, IngestBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Version is not (1 or 2))
            throw new ArgumentException("Only ingest versions 1 and 2 are supported.");
        var now = clock.GetUtcNow();
        if (batch.Version == 1)
        {
            if (batch.ActivityRange is not null || batch.Activities is not null)
                throw new ArgumentException("Version 1 accepts AthleteProfile changes only.");
            if (batch.AthleteProfiles is not { Count: > 0 and <= 100 })
                throw new ArgumentException("Supply between 1 and 100 AthleteProfile changes.");
            foreach (var change in batch.AthleteProfiles)
                AthleteProfileChange.Validate(change, now);
            return await profiles.SaveAsync(userId, batch.AthleteProfiles.Select(change => change!).ToArray(), now, cancellationToken);
        }

        if (batch.ActivityRange is null || batch.Activities is not { Count: <= 5000 }
            || batch.AthleteProfiles is { Count: > 100 })
            throw new ArgumentException("Version 2 requires an ActivityRange and 0 to 5000 Activities, with at most 100 AthleteProfile changes.");
        batch.ActivityRange.Validate();
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
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(batch))));
        await ingest.SaveAsync(userId, (batch.AthleteProfiles ?? []).Select(change => change!).ToArray(),
            batch.ActivityRange, activities, exerciseSets, hash, now, cancellationToken);
        return await status.GetAsync(userId, cancellationToken);
    }
}
