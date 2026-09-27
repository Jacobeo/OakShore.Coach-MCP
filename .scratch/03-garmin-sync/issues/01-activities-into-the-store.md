# 01: Activities into the store, safely

**What to build:** A run started by hand for an explicit date range signs in to
Garmin from the saved token file, lists the athlete's Activities for that range,
and posts them to the ingest endpoint, where they persist. A read tool reports
what is stored and how fresh it is, so a run can be confirmed from a chat client
rather than from the database. Every Garmin request is paced, and the run stops
rather than retries when Garmin rate-limits or challenges it.

This is the first slice through the Python sync and establishes the Python seam.

**Blocked by:** None (can start immediately).

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md).

- [x] A manual run for a given date range signs in from the stored token file
  without an interactive prompt, lists Activities, and posts them to ingest.
- [x] Ingest accepts version 2 batches carrying Activities alongside
  AthleteProfile. Version 1 payloads remain accepted unchanged. Unsupported
  versions and invalid input are rejected without persisting any part of a batch.
- [x] Activities persist with UserId on every table and query, and re-posting the
  same batch leaves the stored result unchanged.
- [x] Garmin's Activity name and type persist with each Activity and appear in
  the most recent Activity summary; an older batch without a name preserves it.
- [x] A read tool reports, per collection, how many records are stored and when
  each was last synced, distinguishing nothing-stored from stored-and-stale. It
  is annotated read-only and returns structuredContent alone.
- [x] Requests to Garmin are paced. A 401, 403 or 429 aborts the run immediately
  with no retry; network errors and 5xx retry with backoff.
- [x] The Python seam replays the spike 01 fixtures at the Garmin client boundary
  and asserts on the batches posted to ingest. Garmin calls live behind one
  module so the seam has a single place to intercept. No automated test contacts
  a live account.
- [x] The .NET seam covers version 2 validation, idempotent upsert and the read
  tool by posting fixtures through ingest and reading back through MCP.
- [x] Any concept introduced that the glossary does not already name is added to
  CONTEXT.md in the same change.
- [x] Architecture checks pass and every added file appears in the solution.
