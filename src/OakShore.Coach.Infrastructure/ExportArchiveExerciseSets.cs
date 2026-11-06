using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Dynastream.Fit;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

/// <summary>
/// The ExerciseSets an ExportArchive's FIT files hold, keyed by the start time of the
/// Activity each was recorded for. Nothing in the archive links a FIT file to an
/// Activity id, so that start time is the only join (ADR 0015).
/// </summary>
public sealed class ExportArchiveExerciseSets
{
    private const ushort UnstatedExerciseName = 65535;

    // Garmin ships one static class of constants per Exercise category rather than one
    // enum, so the category and name a set states are looked up by reflection once.
    private static readonly IReadOnlyDictionary<ushort, string> CategoryNames =
        typeof(ExerciseCategory).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => Convert.ToUInt16(field.GetValue(null)), field => field.Name);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<ushort, string>> ExerciseNames =
        typeof(SetMesg).Assembly.GetTypes().Where(type => type.Name.EndsWith("ExerciseName"))
            .ToDictionary(type => type.Name[..^"ExerciseName".Length],
                type => (IReadOnlyDictionary<ushort, string>)type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .GroupBy(field => Convert.ToUInt16(field.GetValue(null)))
                    .ToDictionary(names => names.Key, names => names.First().Name));

    /// <summary>Read the archive, which may be the zip Garmin sends or the folder it was unpacked to.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ExerciseSetInput>> Read(string archive)
    {
        var byStartTime = new SortedDictionary<string, IReadOnlyList<ExerciseSetInput>>(StringComparer.Ordinal);
        foreach (var (name, open) in ArchiveFiles(archive))
        {
            if (!name.EndsWith(".fit", StringComparison.OrdinalIgnoreCase))
                continue;
            using var file = open();
            if (Activity(file) is { } activity)
                byStartTime[activity.StartTimeUtc] = activity.ExerciseSets;
        }
        return byStartTime;
    }

    private static (string StartTimeUtc, IReadOnlyList<ExerciseSetInput> ExerciseSets)? Activity(Stream file)
    {
        var listener = new FitListener();
        var decode = new Decode();
        decode.MesgEvent += listener.OnMesg;
        try
        {
            if (!decode.IsFIT(file))
                return null;
            file.Position = 0;
            decode.Read(file);
        }
        catch (FitException)
        {
            // An archive that has lost a file to truncation still imports what it holds.
            return null;
        }
        var messages = listener.FitMessages;
        if (messages.FileIdMesgs.FirstOrDefault()?.GetType() != Dynastream.Fit.File.Activity)
            return null;
        if (messages.SessionMesgs.FirstOrDefault()?.GetStartTime()?.GetDateTime() is not { } start)
            return null;
        var exerciseSets = messages.SetMesgs.Select(ExerciseSet).OfType<ExerciseSetInput>().ToArray();
        return exerciseSets.Length == 0 ? null : (Utc(start), exerciseSets);
    }

    private static ExerciseSetInput? ExerciseSet(SetMesg set)
    {
        var setType = set.GetSetType() switch
        {
            Dynastream.Fit.SetType.Active => "ACTIVE",
            Dynastream.Fit.SetType.Rest => "REST",
            _ => null
        };
        if (setType is null)
            return null;
        var categories = Enumerable.Range(0, set.GetNumCategory()).Select(set.GetCategory)
            .Select(category => category is { } value && CategoryNames.TryGetValue(value, out var name)
                && name is not ("Invalid" or "Unknown") ? name : null).ToArray();
        ExerciseInput? exercise = null;
        if (categories.FirstOrDefault() is { } top)
            exercise = new ExerciseInput(Garmin(top), ExerciseName(top, set.GetCategorySubtype(0)));
        // Bodyweight and a classification probability have no FIT counterpart and are left
        // unstated rather than invented (ADR 0016).
        return new ExerciseSetInput(
            setType,
            set.GetRepetitions() is { } repetitions ? repetitions : null,
            Kilograms(set.GetWeight()),
            Exercise: exercise,
            CandidateCount: categories.Count(category => category is not null),
            StartTimeUtc: set.GetStartTime()?.GetDateTime() is { } start ? Utc(start) : null,
            DurationSeconds: Seconds(set.GetDuration()),
            WktStepIndex: set.GetWktStepIndex() is { } wktStepIndex ? wktStepIndex : null,
            Source: ExerciseSetSource.ExportFit);
    }

    private static string? ExerciseName(string category, ushort? subtype) =>
        subtype is { } value && value != UnstatedExerciseName
            && ExerciseNames.TryGetValue(category, out var names)
            && names.TryGetValue(value, out var name) && name != "Invalid"
            ? Garmin(name) : null;

    private static double? Seconds(float? duration) =>
        // FIT counts a set's duration in milliseconds, so three decimal places are exact and
        // the figure matches the one the API reports for the same set.
        duration is null ? null : Math.Round((double)duration.Value, 3);

    private static decimal? Kilograms(float? weight) =>
        // FIT counts weight in sixteenths of a kilogram, so every value the SDK scales to
        // kilograms is exact at the four decimal places the contract allows.
        weight is null ? null : Math.Round((decimal)(double)weight.Value, 4);

    private static string Utc(System.DateTime moment) =>
        moment.ToString("yyyy-MM-dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

    // Garmin's API spells a category or Exercise name in upper snake case, where the SDK's
    // constants are Pascal case; the two name the same values.
    private static string Garmin(string constant)
    {
        var spelling = new StringBuilder(constant.Length + 8);
        for (var index = 0; index < constant.Length; index++)
        {
            if (index > 0 && char.IsUpper(constant[index]) && (!char.IsUpper(constant[index - 1])
                || (index + 1 < constant.Length && char.IsLower(constant[index + 1]))))
                spelling.Append('_');
            spelling.Append(char.ToUpperInvariant(constant[index]));
        }
        return spelling.ToString();
    }

    /// <summary>Each file in the archive, wherever it lies, as its name and how to open it.</summary>
    private static IEnumerable<(string Name, Func<Stream> Open)> ArchiveFiles(string archive)
    {
        if (!Directory.Exists(archive))
        {
            foreach (var file in Listed(Path.GetFileName(archive), () => System.IO.File.OpenRead(archive)))
                yield return file;
            yield break;
        }
        foreach (var path in Directory.EnumerateFiles(archive, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            foreach (var file in Listed(Path.GetFileName(path), () => System.IO.File.OpenRead(path)))
                yield return file;
    }

    private static IEnumerable<(string Name, Func<Stream> Open)> Listed(string name, Func<Stream> open)
    {
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            yield return (name, open);
            yield break;
        }
        using var file = open();
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            if (entry.Name.Length == 0)
                continue;
            foreach (var nested in Listed(entry.Name, () => Copied(entry)))
                yield return nested;
        }
    }

    private static Stream Copied(ZipArchiveEntry entry)
    {
        // Read whole, because seeking back inside a compressed member restarts its decompression.
        var buffer = new MemoryStream();
        using (var member = entry.Open())
            member.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }
}
