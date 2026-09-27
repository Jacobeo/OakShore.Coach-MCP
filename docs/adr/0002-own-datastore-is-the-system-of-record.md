# Our datastore is the system of record, not Garmin

Tools could fetch from Garmin on each call. We keep our own store instead, and
MCP tools read only from it.

The use case is conversational coaching over a training history: read-heavy,
repetitive, and aggregate-shaped across months and years. That is the worst
possible fit for a per-request unofficial API behind bot detection, and an agent
exploring a training block would hit the same endpoints repeatedly.

Decisively, the central domain object — the **Macrocycle** — does not exist in
Garmin at all. Garmin has no concept of a multi-month periodized structure and
no way to export one. Even a perfect Garmin client could not answer "am I on
plan", so a store is required regardless of performance.

## Consequences

- Queries span years and join across domains (strength, sleep, load) in ways
  Garmin's API cannot express.
- Staleness becomes a first-class concern: every response carries the time the
  data was last synced, so the agent can say what it might be missing.
- Historical backfill is a one-off project of its own.
