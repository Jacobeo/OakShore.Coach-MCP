namespace OakShore.Coach.Domain;

/// <summary>
/// The ExerciseSets one ExportArchive's FIT files hold, keyed by the start time of the
/// Activity each file was recorded for, because nothing in the archive names an Activity
/// (ADR 0015). An ExportImport posts a historic Activity and the sets this index holds for
/// its start time in one Backfill; posted later, the sets would be dropped (ADR 0014).
/// </summary>
public sealed record ExportSetIndex(
    IReadOnlyDictionary<string, IReadOnlyList<ExerciseSetInput>> ByActivityStartTime)
{
    public int ExerciseSetCount => ByActivityStartTime.Sum(activity => activity.Value.Count);
}
