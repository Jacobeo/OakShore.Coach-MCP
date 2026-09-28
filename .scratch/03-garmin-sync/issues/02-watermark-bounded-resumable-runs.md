# 02: Watermark, bounded and resumable runs

**What to build:** The sync stops needing a date range. Each run reads the last
date it completed, works forward from there most recent first, commits each day
as it finishes, and stops once it has done a fixed amount of work. Running it
twice in an hour, or missing a week, changes nothing except how much catching up
is left.

**Blocked by:** 01 Activities into the store, safely.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0005](../../../docs/adr/0005-bounded-resumable-sync.md).

- [x] A run with no arguments determines its own range from the last completed
  date. A first run against an empty store begins from a configured earliest date.
- [x] Days are processed most recent first, and each is committed independently,
  so interrupting a run keeps everything already fetched and the next run
  continues from where it stopped.
- [x] A run stops once it reaches a configured ceiling on days processed, leaving
  the watermark where it reached. A long gap closes over several runs.
- [x] Re-running over an already-synced period makes no observable change.
- [x] Watermark advancement, resumption after interruption, the ceiling,
  most-recent-first ordering and idempotence are each asserted at the Python seam.
- [x] CONTEXT.md names the watermark concept if the glossary does not already.
