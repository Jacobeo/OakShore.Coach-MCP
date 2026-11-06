# ExportArchive fixture

A trimmed, anonymised sample of the athlete's real Garmin data export, the
ExportArchive, which drives
[test_import_export_archive.py](../../test_import_export_archive.py) at the
Python ingest seam. Only the Python importer reads it, which is why it lives
here rather than beside the shared API corpus in
[fixtures/garmin](../../../../fixtures/garmin/README.md).

The full export is never committed. This sample was cut from it by hand: the
file names, directory layout, field names and number formats are Garmin's own;
only the records listed below were kept, and identifying values were replaced.

## What is here

`archive/` is laid out as the export unpacks:

| File | Holds |
| --- | --- |
| `DI_CONNECT/DI-Connect-Fitness/athlete@example.invalid_1_summarizedActivities.json` | 4 Activities from the export's first count-chunked file |
| `DI_CONNECT/DI-Connect-Fitness/athlete@example.invalid_301_summarizedActivities.json` | 1 Activity from its second |
| `DI_CONNECT/DI-Connect-Wellness/<from>_<to>_1_sleepData.json` | 2 nights from each of two date-chunked files |
| `DI_CONNECT/DI-Connect-Aggregator/UDSFile_<from>_<to>.json` | the same 2 days' daily rollups from each of two date-chunked files |
| `DI_CONNECT/DI-Connect-Uploaded-Files/UploadedFiles_0-_Part1.zip` | a nested zip holding one 95-byte FIT file |

The Activities are 24272601315 (strength), 24313465171 (running) and
24174672446 (indoor cycling), which are also in the API corpus's Activity list,
so the tests can compare what the two sources post for the same Activity; plus
16155825735 (walking, 2024) and 16867435216 (yoga, 2024, which has no distance).
The days are 2026-06-08, 2026-06-09, 2026-09-15 and 2026-09-16.

The FIT file is a weight-scale file, not an Activity, kept byte for byte: it
carries no identifying field (its serial number is Garmin's generic `1`). Real
exports hold thousands of FIT files in these nested zips; this one exists so the
layout is real and so the per-set FIT parsing of
[ticket 10](../../../../.scratch/03-garmin-sync/issues/10-export-fit-per-set-detail.md)
has a file to skip.

## What was changed, and what was not

The rules follow the API corpus's
[scrub table](../../../../spikes/strength-data-spike/findings.md#what-was-scrubbed-and-what-was-not):

| Field or name | Becomes | Why |
| --- | --- | --- |
| The account's email, in file names | `athlete@example.invalid` | account identifier |
| The profile id, in file names | `1` | account identifier |
| `userProfileId`, `userProfilePK`, `userProfilePk`, `deviceId`, at any depth | `1`, but `0` and null kept | account and device identifiers |
| `uuid` (daily rollups) | a placeholder, one per day | per-day identifier |
| `uuidMsb`, `uuidLsb` (Activities) | `0` | per-Activity identifier |
| `name` (Activities) | the Activity's type | free text naming places |
| `locationName` | `"Somewhere"` | locates the session |
| every `…Latitude` and `…Longitude` | `null` | locates the session to the metre |
| `splits`, `splitSummaries` | removed | per-lap series carrying positions; absent from half the real Activities anyway |

Kept as Garmin wrote them: `activityId`, every timestamp and `timeZoneId`, and
every physiological figure, for the reasons the API corpus gives. After trimming,
no file name or byte of the sample held any of the values replaced.
