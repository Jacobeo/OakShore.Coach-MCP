# Export data is backfill-only

The ExportArchive and the sync both deliver Activities, SleepNights and
DailySummaries, and their dates overlap. The export is a snapshot taken whenever
the athlete requested it, in its own shape, translated by the importer; the API
is what the sync reads every night and re-reads on demand. Ticket 09 first posted
the export as ordinary version 3 batches. An overlapping day then took the
export's figures and archived record in place of the API's, and a first import
set the Activity and wellness sync times to when it ran: `get_sync_status`
reported fresh data while the recent days were exactly as stale as before.

**Export data enters the store only as a Backfill, and ingest enforces both of
its rules.** An IngestBatch carries a `backfill` flag, and the importer sets it on
every batch it posts.

1. **A Backfill fills gaps only.** An Activity, SleepNight or DailySummary already
   stored is left exactly as it is: no figure, no archived `garminResponse`, no
   ExerciseSet or time in zones changes. API-derived data always wins, as
   [ticket 10](../../.scratch/03-garmin-sync/issues/10-export-fit-per-set-detail.md)
   already requires for per-set detail.
2. **A Backfill is not a sync.** It leaves no IngestBatch record behind, so it
   moves no `lastSyncedAt` and no latest day reached. Freshness
   ([ADR 0002](0002-own-datastore-is-the-system-of-record.md)) says when the store
   last heard from Garmin's API, which is what tells the agent what it may be
   missing; years-old history adds nothing to that. A store holding only
   backfilled records reports their counts with no sync time, which is true.

A Backfill carries Activities, SleepNights and DailySummaries only, in version 2
or 3. AthleteProfile changes, SportProfiles and daily form each carry a sync time
of their own, and the export supplies none of them, so ingest rejects them.

The flag is a field rather than a new version because versions are a ladder of
what a batch carries; how ingest treats a batch is a separate question that
applies on any rung. The rules live in .NET, not in the importer, because only
ingest knows what is stored, and the store is the system of record.

## Consequences

- Re-importing an archive changes nothing by rule 1. Since a Backfill records no
  batch, that, not the batch hash, is what makes the import idempotent.
- Where the two sources disagree about a stored record, the API's version stands
  even if the export's is the correction. Re-syncing that day through the API
  with `--from-date` and `--to-date` is the way to change it.
- A day backfilled before the sync reaches it is replaced by the sync's batch for
  that day, as any synced delivery replaces a stored day, so the API wins in
  either order.
- A Backfill's ActivityRange only bounds the dates it may carry; it states
  nothing about what was synced. The importer posts whole calendar months.
- The flag is part of every batch's hash, so a batch replayed across the release
  that added it counts once as a new delivery and moves freshness, which a real
  sync replaying it has earned.
