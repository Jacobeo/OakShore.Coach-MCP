using System.Text.Json;
using System.Text.Json.Serialization;
using OakShore.Coach.Infrastructure;

// Read an ExportArchive's FIT files and write the ExportSetIndex the ExportImport merges
// into its Backfills. Reads nothing but the archive and writes nothing but the index.
if (args.Length != 2)
{
    await Console.Error.WriteLineAsync(
        "usage: OakShore.Coach.ExportFit <archive zip or folder> <exercise-sets.json>");
    return 2;
}
var (archive, output) = (args[0], args[1]);
if (!File.Exists(archive) && !Directory.Exists(archive))
{
    await Console.Error.WriteLineAsync($"{archive} does not exist");
    return 1;
}

var exerciseSets = new ExportArchiveExerciseSets().Read(archive);
var json = JsonSerializer.Serialize(exerciseSets, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
});
await File.WriteAllTextAsync(output, json + "\n");

Console.WriteLine($"Wrote {exerciseSets.Sum(activity => activity.Value.Count)} ExerciseSets for"
    + $" {exerciseSets.Count} Activities to {output}");
return 0;
