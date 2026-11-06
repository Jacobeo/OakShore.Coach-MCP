# 11: ExportImport, rehearsed locally, then run in production

**What to build:** An ExportImport of the athlete's real ExportArchive into the hosted
store. It is rehearsed first against a local copy of the whole system in Docker
Desktop, where the result can be debugged and the stored data inspected, and the
production run happens only on a deployment known to carry the Backfill rules.

**Blocked by:** 09 Export archive backfill; 10 Export FIT parsing for historic
per-set detail (only the rehearsal and the production run wait for it; the
local environment can be built now).

**Status:** ready-for-human

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0014](../../../docs/adr/0014-export-data-is-backfill-only.md),
README sections "Run the API", "Deploy the body-weight slice" and "Export archive backfill".

## Local environment

Nothing documented today runs the API against a database you can open. `dotnet
test` starts a throwaway SQL Server container per run, and "Run the API" assumes
a database that already exists.

- [ ] A committed, documented Docker Desktop setup starts SQL Server with a
  persistent volume and creates the Coach database. The API runs from Visual
  Studio or `dotnet run` against it, so it can be debugged with breakpoints, or
  in a container built from the repository's `Dockerfile`.
- [ ] The importer can post to the local API with a token the local API accepts,
  and no change to production auth. A likely route is to configure the local API
  with the hosted WorkOS authority and audience, so the sync's saved sign-in
  works unchanged. That route is to be confirmed, not assumed.
- [ ] The stored data can be inspected: a documented connection from Visual
  Studio's SQL Server Object Explorer (or SSMS), and `get_sync_status` read
  through an MCP client against the local API.
- [ ] Every new file is in `coach-mcp.sln`, and the README documents starting,
  resetting and stopping the environment. No real data or secrets are committed.

## Local rehearsal

- [ ] With the nightly sync's behaviour reproduced locally (some recent days and
  Activities synced first), an ExportImport of the real `garmin dump/` runs, and
  the results are recorded below as a comment:
  - the importer's closing counts, and how many records were left out;
  - the row counts by kind;
  - spot checks of a few historic Activities and days against Garmin Connect;
  - confirmation that the synced days and Activities kept their values, and
    that `get_sync_status` sync times did not move.
- [ ] A second ExportImport changes nothing.
- [ ] Any defect found is fixed in its own change before the production run.

## Production run

- [ ] `main` is pushed, and the image is built from a clean checkout of that
  commit (`publish-image.ps1` builds from the working tree).
- [ ] That image is deployed with `deploy.ps1`. It is confirmed live by comparing
  the active revision's image digest with the one just published, then by a
  read-only `smoke.ps1` run. An older server would ignore `"backfill": true` and
  treat the ExportImport as a sync, which is exactly what ADR 0014 rules out, so this
  check is the gate.
- [ ] Before the ExportImport, the time is noted for Azure SQL point-in-time restore,
  and `get_sync_status` is recorded.
- [ ] The ExportImport runs against the hosted ingest. Afterwards, `get_sync_status`
  sync times are unchanged and the counts have grown by what the rehearsal
  predicted.
- [ ] The nightly sync's next scheduled run succeeds after the ExportImport.

## Why this waits for ticket 10

Under ADR 0014, a Backfill drops everything for an Activity the store already
holds, ExerciseSets included. Had this ExportImport run first, ticket 10's FIT-derived
ExerciseSets for every historic strength Activity would arrive too late and be
dropped. Decided 2026-11-06: the rehearsal and the production run wait for
ticket 10, so each historic Activity is imported once, together with its
per-set detail, and ticket 10 needs no rule for filling in missing ExerciseSets.
