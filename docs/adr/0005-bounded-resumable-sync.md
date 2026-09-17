# Sync does bounded, resumable work per run

Garmin blocks by IP address, and a long gap is exactly when a naive sync would
make the most requests in the shortest time. The sync is therefore shaped so
that a large gap can never produce a large burst.

**Activities are cheap, wellness is expensive.** An activity list for an
arbitrary date range is one call plus paging, and it already carries the strength
set and rep totals, so listing history costs almost nothing. Enriching each
activity costs roughly one further call — exercise sets for strength, heart-rate
time in zones for cardio. Sleep, HRV, body battery, stress, daily summary,
training status and training readiness are all per-date endpoints, at roughly six
calls for every day synced. That asymmetry drives the whole design: cost scales
with days of wellness, not with activities.

**Three mechanisms:**

1. **A watermark, not a schedule.** Each run reads the last successfully synced
   date and works forward from there. Missing a night, or running twice in an
   hour, changes nothing. Each day is committed independently, so a failure part
   way through keeps everything already fetched.
2. **A hard ceiling on work per run.** A run fetches at most a fixed number of
   wellness-days, most recent first, then stops and leaves the watermark where it
   got to. A six-month gap heals over several nights rather than in one
   multi-hour session. Recent data — the data the coach actually reasons over —
   arrives on the first run.
3. **Paced requests and a circuit breaker.** A few seconds between per-day calls.
   On 401, 403 or 429 the run aborts immediately and notifies; it never retries,
   because retrying a rate limit extends the block. Retries apply only to network
   errors and 5xx.

**Bulk history comes from the export, not the API.** Multi-year backfill through
per-day endpoints would be tens of thousands of calls. Garmin's own data export
delivers the same history in one archive with zero API calls, so the API is only
ever used for the recent window.

## Consequences

- A two-week absence costs about seven minutes of paced syncing. A month costs
  about fifteen. Neither is a burst.
- After a long absence the store is knowingly incomplete for older days for a
  few nights. Responses carry their own freshness, so the agent can say so.
- The per-run ceiling looks like an arbitrary limit to a future reader. It is
  deliberate: it is the thing that prevents a burst.
