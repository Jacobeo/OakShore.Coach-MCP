# 10: Export FIT parsing for historic per-set detail

**What to build:** Historic strength Activities gain the same per-ExerciseSet
detail that newly synced ones have, by parsing the raw FIT files carried inside
the export archive.

**Blocked by:** 09 Export archive backfill; 03 Strength Activities with
per-ExerciseSet detail.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0003](../../../docs/adr/0003-python-for-garmin-dotnet-for-the-rest.md).

- [x] FIT files inside the archive's nested archives are parsed with Garmin's
  official .NET FIT SDK, and their set messages produce ExerciseSets in the shape
  ticket 03 established.
- [x] FIT weight is converted to the same canonical unit at the same boundary as
  the API path, so the two source encodings cannot diverge in the store.
- [x] Where an Activity exists in both sources, the API-derived detail wins, and
  the source of a stored ExerciseSet is recoverable.
- [x] A file whose type is not an activity is skipped without failing the import.
- [x] Asserted at the ingest seam with one real FIT file committed as a fixture.

## Comments

Since ticket 09, export data is backfill-only ([ADR 0014](../../../docs/adr/0014-export-data-is-backfill-only.md)):
a Backfill drops everything for an Activity the store already holds, ExerciseSets
included. A historic Activity's FIT-derived ExerciseSets must therefore arrive in
the same Backfill as the Activity itself; posted later, they would be dropped.
Ticket 11's production ExportImport waits for this ticket for that reason.

2026-11-06: Implemented. FIT parsing is a local .NET extractor, not ingest:
`OakShore.Coach.ExportFit` is a thin console over `ExportArchiveExerciseSets` in
`OakShore.Coach.Infrastructure`, writing an ExportSetIndex that
`import_export_archive.py` merges into the Activity's own Backfill. Posting raw
FIT bytes to ingest was rejected — the real archive holds 22,522 FIT files of
which 605 are Activities, and nothing in it names an Activity.

Judgment calls, each open to correction. The source is `ExerciseSetSource`, a
`source` field of `GARMIN_API` or `EXPORT_FIT` on every ExerciseSet, defaulting
to the API so every batch posted before this release stays valid; the alternative,
inferring it from an absent `topProbability`, would be a guess rather than a
recoverable fact. "The API-derived detail wins" needed no new precedence logic:
ADR 0014's gap-filling filter already drops a Backfill's ExerciseSets for a stored
Activity, and the positional replace already lets a later API sync overwrite
backfilled sets — both now asserted. Category and Exercise name come from slot 0
of FIT's three-slot classification array, upper-snake-cased from the SDK's
constants; checked against the API corpus for every set of the five Activities in
both sources, 156 of 156 names agree. `candidateCount` is the count of filled
slots, which matches the API's `len(exercises)` for those; `topProbability` has no
FIT counterpart and is left absent.

Two findings became ADRs. A FIT file joins its Activity by `session.start_time`,
exact for 137 of the athlete's 149 strength files and within the same minute for
the other 12, whose export records are dated to the whole minute
([ADR 0015](../../../docs/adr/0015-export-fit-files-join-activities-by-start-time.md));
`file_id.time_created` drifts 0 to 2 seconds and is not the join. And FIT cannot
state a bodyweight load at all — Garmin writes the same three bodyweight sets as
one absent weight and two zeroes where the API reports `-1`
([ADR 0016](../../../docs/adr/0016-fit-cannot-state-a-bodyweight-load.md)) — so
the FIT path reports what FIT holds and the source field says which reading it is.

Not captured here: the HR series for time in zones, which the build order's step 4
also names and no ticket carries; and reconciling the full history against Garmin
Connect, which is ticket 11's production ExportImport.
