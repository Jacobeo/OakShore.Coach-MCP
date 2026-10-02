using System.Globalization;

namespace OakShore.Coach.Domain;

public sealed record Exercise(string Category, string? Name);

public sealed record ExerciseSet(
    string SetType,
    int? Repetitions,
    decimal? WeightKg,
    bool Bodyweight,
    Exercise? Exercise,
    int? CandidateCount,
    double? TopProbability,
    DateTimeOffset? StartTimeUtc,
    double? DurationSeconds,
    int? WktStepIndex)
{
    private const string ActiveSetType = "ACTIVE";

    public bool Working => SetType == ActiveSetType && Repetitions is > 0;
    // Zero repetitions on an active set is the only reliable cancellation signal;
    // duration overlaps between cancelled and working sets. An active set with no
    // repetition count recorded no work either, so it reads the same way.
    public bool Cancelled => SetType == ActiveSetType && Repetitions is not > 0;
}

public sealed record StrengthDetail(
    IReadOnlyList<ExerciseSet> ExerciseSets,
    int WorkingExerciseSets,
    int CancelledExerciseSets,
    decimal VolumeKg,
    double? AverageRepetitions)
{
    public static StrengthDetail? From(IReadOnlyList<ExerciseSet> exerciseSets)
    {
        if (exerciseSets.Count == 0)
            return null;
        var working = exerciseSets.Where(exerciseSet => exerciseSet.Working).ToArray();
        // A bodyweight set adds its repetitions to the average but no stated kilograms
        // to the volume; what the athlete's mass was is decided where a figure is read.
        return new StrengthDetail(
            exerciseSets,
            working.Length,
            exerciseSets.Count(exerciseSet => exerciseSet.Cancelled),
            working.Sum(exerciseSet => exerciseSet.Repetitions!.Value * (exerciseSet.WeightKg ?? 0m)),
            working.Length == 0 ? null : working.Average(exerciseSet => (double)exerciseSet.Repetitions!.Value));
    }
}

public sealed record ExerciseInput(string? Category = null, string? Name = null);

public sealed record ExerciseSetInput(
    string? SetType,
    int? Repetitions = null,
    decimal? WeightKg = null,
    bool? Bodyweight = null,
    ExerciseInput? Exercise = null,
    int? CandidateCount = null,
    double? TopProbability = null,
    string? StartTimeUtc = null,
    double? DurationSeconds = null,
    int? WktStepIndex = null)
{
    public ExerciseSet ToExerciseSet()
    {
        if (string.IsNullOrWhiteSpace(SetType) || SetType.Length > 50 || SetType != SetType.Trim())
            throw new ArgumentException("ExerciseSet setType must be 1 to 50 nonblank characters without surrounding whitespace.");
        if (Repetitions is < 0 or > 99999)
            throw new ArgumentException("ExerciseSet repetitions must be between 0 and 99999.");
        if (WeightKg is { } weight && (weight < 0 || weight >= 1_000_000m || decimal.Round(weight, 4) != weight))
            throw new ArgumentException("ExerciseSet weightKg must be nonnegative kilograms below 1000000, with at most four decimal places.");
        if (Bodyweight == true && WeightKg is not null)
            throw new ArgumentException("A bodyweight ExerciseSet states its load as the athlete's own mass and cannot also carry weightKg.");
        Exercise? exercise = null;
        if (Exercise is not null)
        {
            // A category with no name is a complete Exercise, not a missing one.
            if (string.IsNullOrWhiteSpace(Exercise.Category) || Exercise.Category.Length > 100
                || Exercise.Category != Exercise.Category.Trim())
                throw new ArgumentException("Exercise category must be 1 to 100 nonblank characters without surrounding whitespace.");
            if (Exercise.Name is not null && (string.IsNullOrWhiteSpace(Exercise.Name)
                || Exercise.Name.Length > 100 || Exercise.Name != Exercise.Name.Trim()))
                throw new ArgumentException("Exercise name must be 1 to 100 nonblank characters without surrounding whitespace when supplied.");
            exercise = new Exercise(Exercise.Category, Exercise.Name);
        }
        if (CandidateCount is < 0 or > 999)
            throw new ArgumentException("ExerciseSet candidateCount must be between 0 and 999.");
        if (TopProbability is { } probability && (!double.IsFinite(probability) || probability < 0 || probability > 100))
            throw new ArgumentException("ExerciseSet topProbability must be between 0 and 100.");
        DateTimeOffset? start = null;
        if (StartTimeUtc is not null)
        {
            if (!DateTimeOffset.TryParseExact(StartTimeUtc,
                    ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                throw new ArgumentException("ExerciseSet startTimeUtc must be a UTC timestamp ending in Z.");
            start = parsed;
        }
        if (DurationSeconds is { } duration && (!double.IsFinite(duration) || duration < 0 || duration >= 1e9))
            throw new ArgumentException("ExerciseSet durationSeconds must be finite, nonnegative and below 1000000000.");
        if (WktStepIndex is < 0 or > 99999)
            throw new ArgumentException("ExerciseSet wktStepIndex must be between 0 and 99999.");
        return new ExerciseSet(SetType, Repetitions, WeightKg, Bodyweight == true, exercise,
            CandidateCount, TopProbability, start, DurationSeconds, WktStepIndex);
    }
}
