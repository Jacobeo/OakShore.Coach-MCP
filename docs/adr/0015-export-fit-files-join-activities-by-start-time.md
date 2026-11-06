# An export FIT file joins its Activity by start time

The ExportArchive carries per-ExerciseSet detail only inside the raw FIT files in
its nested `UploadedFiles_*.zip` archives. Nothing in those files or their names
names an Activity: a real file is `<email>_<uploadId>.fit`, and that upload id
appears nowhere in the archive's `summarizedActivities` records. The only shared
fact is when the Activity started.

**A FIT file is joined to an Activity by the start time its `session` message
states**, matched against the Activity's own start time.

`file_id.time_created` is not the join. Across the athlete's 149 strength FIT
files it sits 0 to 2 seconds after `session.start_time`, so it would miss or
mismatch; `session.start_time` is what Garmin itself reports as the Activity's
start.

**The match is on the second, with the whole minute as a fallback.** 137 of those
149 files agree with the export's `startTimeGmt` to the second. The other 12 are
Activities the export dates to the whole minute — `:00` seconds — while their FIT
file states the second, 1 to 45 seconds later. So an Activity dated to a whole
minute with no exact match takes the one FIT file recorded in that minute. A
minute holding more than one FIT file is left unmatched rather than guessed at;
none of the 605 Activities in the export shares a start minute with another.

Matching happens in the importer, because it is the importer that builds the
batch a set has to ride in: a Backfill naming an Activity the store already holds
is dropped whole ([ADR 0014](0014-export-data-is-backfill-only.md)), per-set
detail included.

The extractor decides only which start times the ExportSetIndex can state, and it
applies the same rule to the narrower case it can see: the index is keyed by start
time, so a second two Activity FIT files state names neither Activity's sets and is
left out of the index rather than filled by whichever file was read last.

## Consequences

- An Activity whose FIT file the export omits keeps the detail it has, or none.
  The import does not fail over it.
- Two Activities started in the same minute would both go unenriched. That costs
  detail and never attributes a set to the wrong Activity.
- Garmin changing how it rounds an export's start times changes which Activities
  match. The rule is asserted at the Python seam against both readings.
