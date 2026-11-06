# 09: Export archive backfill

**What to build:** Years of history become queryable by ingesting Garmin's
official data export — activity summaries and daily wellness — with no API calls
at all, so the long-horizon questions are answerable without ever risking a block.

**Blocked by:** 01 Activities into the store, safely.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0005](../../../docs/adr/0005-bounded-resumable-sync.md).

- [x] A local export archive is ingested through the same authenticated ingest
  endpoint. The importer makes no Garmin API calls.
- [x] Files are located by globbing rather than by assumed names, and every
  date-chunked file of a kind is read rather than only the first.
- [x] Nested archives are opened to reach the files inside them.
- [x] Every field is treated as optional: an archive missing one is ingested
  rather than rejected, asserted with a deliberately trimmed sample, because
  archive structure varies between exports.
- [x] Importing is idempotent. Re-importing the same archive changes nothing, and
  an Activity already synced from the API is not duplicated.
- [x] A trimmed sample archive is committed as a fixture and drives the test at
  the ingest seam.
- [x] The README documents how to request an export and how to run the import.

## Comments

Export data is backfill-only: it fills gaps without overwriting anything the API
stored, and never counts as a sync ([ADR 0014](../../../docs/adr/0014-export-data-is-backfill-only.md)).
The importer is Python in `src/sync/`, reusing the sync's sign-in and ingest
client. The golden file `tests/python/fixtures/garmin-export/ingest-batches.json`
is reproduced by the Python tests and posted by the .NET ingest-seam tests.
Time in zones, TrainingLoad and VO2max are not imported from the export.
