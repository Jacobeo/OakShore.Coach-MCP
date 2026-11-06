using System.Text.Json;
using System.Text.Json.Serialization;
using OakShore.Coach.Infrastructure;

// Read an ExportArchive's FIT files and write the ExportSetIndex the ExportImport merges
// into its Backfills. Reads nothing but the archive and writes nothing but the index.
if (args.Length != 2)
{
    await Console.Error.WriteLineAsync(
        "usage: OakShore.Coach.ExportFit <archive zip or folder> <export-set-index.json>");
    return 2;
}
var (archive, output) = (args[0], args[1]);
if (!File.Exists(archive) && !Directory.Exists(archive))
{
    await Console.Error.WriteLineAsync($"{archive} does not exist");
    return 1;
}

var index = new ExportArchiveExerciseSets().Read(archive);
// The index is written as its own start-time-to-sets mapping, which is what the ExportImport reads.
var json = JsonSerializer.Serialize(index.ByActivityStartTime, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
});
await File.WriteAllTextAsync(output, json + "\n");

Console.WriteLine($"Wrote {index.ExerciseSetCount} ExerciseSets for"
    + $" {index.ByActivityStartTime.Count} Activities to {output}");
return 0;
