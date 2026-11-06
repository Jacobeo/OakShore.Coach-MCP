# ExportArchive fixture

A trimmed, anonymised sample of the athlete's real Garmin data export, the
ExportArchive, which drives
[test_import_export_archive.py](../../test_import_export_archive.py) at the
Python ingest seam, and the FIT parsing at the .NET one. Only the export path
reads it, which is why it lives here rather than beside the shared API corpus in
[fixtures/garmin](../../../../fixtures/garmin/README.md).

Two generated files sit beside it, where the two runtimes meet.
[export-set-index.json](export-set-index.json) is the ExportSetIndex
`OakShore.Coach.ExportFit` writes for `archive/`; the .NET ingest tests assert the
extractor reproduces it, and the Python importer reads it.
[ingest-batches.json](ingest-batches.json) is what the importer then posts; the
Python test asserts the importer reproduces it exactly, and the .NET ingest tests
post it. After a deliberate change to what either produces, regenerate it —
`dotnet run --project src/OakShore.Coach.ExportFit -- tests/python/fixtures/garmin-export/archive tests/python/fixtures/garmin-export/export-set-index.json`
and then `python tests/python/regenerate_ingest_batches.py` — and review the diff.

The full export is never committed. This sample was cut from it by hand: the
file names, directory layout, field names and number formats are Garmin's own;
only the records listed below were kept, and identifying values were replaced.

## What is here

`archive/` is laid out as the export unpacks:

| File | Holds |
| --- | --- |
| `DI_CONNECT/DI-Connect-Fitness/athlete@example.invalid_1_summarizedActivities.json` | 5 Activities from the export's first count-chunked file |
| `DI_CONNECT/DI-Connect-Fitness/athlete@example.invalid_301_summarizedActivities.json` | 1 Activity from its second |
| `DI_CONNECT/DI-Connect-Wellness/<from>_<to>_1_sleepData.json` | 2 nights from each of two date-chunked files |
| `DI_CONNECT/DI-Connect-Aggregator/UDSFile_<from>_<to>.json` | the same 2 days' daily rollups from each of two date-chunked files |
| `DI_CONNECT/DI-Connect-Uploaded-Files/UploadedFiles_0-_Part1.zip` | a nested zip holding three FIT files |

The Activities are 23898645459 and 24272601315 (strength), 24313465171
(running) and 24174672446 (indoor cycling), which are also in the API corpus's
Activity list, so the tests can compare what the two sources post for the same
Activity; plus 16155825735 (walking, 2024) and 16867435216 (yoga, 2024, which
has no distance). The days are 2026-06-08, 2026-06-09, 2026-09-15 and
2026-09-16.

The nested zip holds the three FIT cases the per-set parsing has to tell apart.
Real exports hold thousands of these in these zips, of which only a few hundred
are Activities.

| FIT file | Is |
| --- | --- |
| `athlete@example.invalid_131826115348_ws_20220408_080745.fit` | a 95-byte weight-scale file, kept byte for byte: the file the parse must skip without failing. It carries no identifying field — its serial number is Garmin's generic `1`. |
| `athlete@example.invalid_468332270391.fit` | the strength Activity 23898645459, re-encoded the same way, with 30 `set` messages. Three of them are the bodyweight calf raises ADR 0016 rests on: Garmin wrote one with no `weight` field and two with `weight` of zero, where the API reports `-1` for all three. Its `file_id.time_created` also sits one second after `session.start_time`, the drift ADR 0015 rules out as the join. |
| `athlete@example.invalid_479519066433.fit` | the strength Activity 24272601315, re-encoded from the real file with the SDK's `Encode` and holding only what the parse reads: `file_id`, the `session` message the Activity's start time comes from, and its 32 `set` messages. |

Both Activities are in `summarizedActivities` here and in the API corpus as
`activity-service--exerciseSets__activity-<id>.json`, so the tests can compare
FIT-derived and API-derived per-set detail for the same Activity — including
what each source says about a bodyweight load
([ADR 0016](../../../../docs/adr/0016-fit-cannot-state-a-bodyweight-load.md)).
Garmin names a real export's Activity FIT files `<email>_<uploadId>.fit`, and
that upload id appears nowhere else in the export, so start time is the only join
([ADR 0015](../../../../docs/adr/0015-export-fit-files-join-activities-by-start-time.md)).

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
| `locationName` | `"Somewhere"` | locates the Activity |
| every `…Latitude` and `…Longitude` | `null` | locates the Activity to the metre |
| `splits`, `splitSummaries` | removed | per-lap series carrying positions; absent from half the real Activities anyway |
| `file_id.serial_number`, in each strength FIT file | `1` | identifies the athlete's own watch, as `deviceId` does |
| every message of each strength FIT file but `file_id`, `session` and `set` | removed | thousands of `record` messages of per-second heart rate and position, which locate the Activity, and every other kind — `activity`, `event`, `device_info`, `workout` and `sport` among them — none of which the parse reads |
| every `file_id` and `session` field but `type`, `manufacturer`, `serial_number`, `time_created`, `start_time`, `sport` and `sub_sport` | removed | not read; `product` among them names the watch model |

Kept as Garmin wrote them: `activityId`; the upload id in each FIT file's name
(`131826115348`, `468332270391`, `479519066433`), a per-upload file id rather
than the account's; every timestamp and `timeZoneId`; every field of each
strength file's `set` messages; and every physiological figure, for the reasons
the API corpus gives.
After trimming, no file name or byte of the sample held any of the values
replaced.
