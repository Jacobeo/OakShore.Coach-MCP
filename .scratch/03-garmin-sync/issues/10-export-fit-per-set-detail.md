# 10: Export FIT parsing for historic per-set detail

**What to build:** Historic strength Activities gain the same per-ExerciseSet
detail that newly synced ones have, by parsing the raw FIT files carried inside
the export archive.

**Blocked by:** 09 Export archive backfill; 03 Strength Activities with
per-ExerciseSet detail.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0003](../../../docs/adr/0003-python-for-garmin-dotnet-for-the-rest.md).

- [ ] FIT files inside the archive's nested archives are parsed with Garmin's
  official .NET FIT SDK, and their set messages produce ExerciseSets in the shape
  ticket 03 established.
- [ ] FIT weight is converted to the same canonical unit at the same boundary as
  the API path, so the two source encodings cannot diverge in the store.
- [ ] Where an Activity exists in both sources, the API-derived detail wins, and
  the source of a stored ExerciseSet is recoverable.
- [ ] A file whose type is not an activity is skipped without failing the import.
- [ ] Asserted at the ingest seam with one real FIT file committed as a fixture.
