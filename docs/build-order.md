# Build order

Ordered so that the two largest unknowns are attacked first, before either can
waste a week. Terminology follows [CONTEXT.md](../CONTEXT.md); decisions are
recorded in [docs/adr](./adr).

Each step states the risk it retires. A step is not done until its "done when"
is demonstrably true.

## Step 1 — Spike: is the strength data real?

**Risk retired:** the differentiated half of this project assumes your Garmin
account holds exercise names, reps and weights. If it does not, the schema and
several tools change shape.

Throwaway Python script, run from home, not kept:

- Log in interactively with `python-garminconnect`, handle MFA, save the token
- `get_activities_by_date` for the last 90 days
- For one strength Activity: dump `get_activity_exercise_sets(id)` raw
- For one run: dump `get_activity_hr_in_timezones(id)` raw
- Also dump `totalSets` / `activeSets` / `totalReps` / `summarizedExerciseSets`
  straight off the list entry

**Done when** you can answer, from real output: is `weight` populated or null?
Is `exercises[].name` a real exercise or `UNKNOWN`? Did your on-watch corrections
survive into the API? Is `summarizedExerciseSets` ever non-empty?

Lives in `spikes/`, committed for the record, never imported.

## Step 2 — Vertical slice with no Garmin in it

**Risk retired:** the whole hosting, auth and client path. Until a real ChatGPT
conversation reaches a real tool on Azure, every later step is built on hope.

- Datastore: Azure SQL, free offer. Schema carries `UserId` from the first table
- `Coach.Api`: ASP.NET Core, `ModelContextProtocol.AspNetCore`, stateless,
  `MapMcp("/mcp")`, separate `/health` for probes
- OAuth against a hosted identity provider (WorkOS or Stytch), protected
  resource metadata published, audience validated
- Two tools only: `get_athlete_profile`, `update_athlete_profile`
- Authenticated ingest endpoint, no callers yet
- Deployed to Container Apps, consumption, scale to zero, image on ghcr.io,
  logs destination `none`, CORS enabled

**Done when** you add the connector in ChatGPT developer mode, ask it your body
weight, and it answers from Azure. Then do the same from your phone.

Tool descriptions are load-bearing here, not documentation: ChatGPT's safety
scanner rejects connectors over wording, and health data is exactly the trigger
area. Return `structuredContent` only, with no duplicate serialized JSON block.

## Step 3 — The sync engine

**Risk retired:** rate limiting and IP blocking, the failure mode that is
painful to recover from.

Python, on your PC, per [ADR 0005](./adr/0005-bounded-resumable-sync.md):

- Watermark table; every run works forward from the last date completed, one day
  committed at a time
- Hard ceiling on wellness-days per run, most recent first
- Paced requests; abort and notify on 401, 403, 429; retry only network and 5xx
- Activities: list by date range, then one enrichment call per *strength*
  Activity for exercise sets. Time in zones is read off the list entry and the
  zone boundaries are fetched once per run, per
  [ADR 0008](./adr/0008-one-heart-rate-zone-set.md)
- Wellness per day: sleep, HRV, body battery, stress, daily summary, training
  status, training readiness
- POSTs batches to the ingest endpoint. Never touches the database directly
- ntfy notification on final failure
- Windows Task Scheduler, daily, with "run as soon as possible after a missed
  start"

**Done when** a deliberately stale watermark causes a paced catch-up that
completes without a 429, and killing it mid-run loses nothing on the next run.

## Step 4 — History backfill from the export

**Risk retired:** a decade of history through per-day endpoints would be tens of
thousands of calls.

- Parse the export archive: `summarizedActivities.json` for activity history,
  nested `UploadedFiles_*.zip` for raw FIT
- `Garmin.FIT.Sdk` in .NET for FIT parsing: `set` messages for per-set reps and
  weight, HR series for time in zones computed against `get_heart_rate_zones()`
- Defensive field access throughout; archive structure varies between exports
- Glob every matching file; these are date-range chunked, never one per type

**Done when** your full history is queryable and activity counts reconcile
against Garmin Connect.

## Step 5 — Program and Phases

- `set_program`: the model parses prose into structure and posts it. The server
  never calls a model
- `get_program`: Program, Phases, and which Phase today falls in
- Structure only dates, modalities and sessions per week. Phase focus stays prose
- `Constraint` support on the profile, with expiry

**Done when** your six-month plan is loaded by pasting it, and the agent can
state which Phase you are in and what it asks for this week.

## Step 6 — Looking back

- `get_training_summary(from, to)`: counts by modality, volume, strength detail,
  Adherence
- `get_current_form`: TrainingLoad, ACWR, TrainingReadiness, HRV, VO2max, sleep
- Every response carries its own data freshness

**Done when** the agent answers "how much have I actually trained this month,
and how does it compare to the plan" without you checking it by hand.

## Step 7 — Looking forward

The use case that started the project.

- `prescribe(from, to, constraints?)`: writes ScheduledWorkouts into the store
- `get_scheduled_workouts(from, to)`
- Revision: a new Constraint re-prescribes the remaining days

**Done when** a Sunday evening conversation produces next week's sessions, and
"I took a knock and only have two evenings" produces a sensible rewrite that
preserves the Phase's intent.

## Step 8 — Write back to Garmin

Last, because it is the only step that can damage anything, and by now the
prescriptions are known to be good.

Per [ADR 0004](./adr/0004-garmin-writes-are-additive-only.md): additive and
reversible only. Create Workout, schedule it, push to device. Validate every
exercise against the bundled catalog first. Never write to a recorded Activity.

Note the push runs from the home sync, not from Azure, and that write tools are
reported to fail from the ChatGPT mobile app — so treat the desktop as the place
where scheduling happens.

**Done when** a prescribed strength session appears on your watch with the
prescribed weights, and the resulting Activity comes back with its Exercise
attached — the category, and a name where the Exercise has one. A null `name`
against a populated `category` is the answer for the flat bench press, not a
failure ([findings](../spikes/strength-data-spike/findings.md)).

## Layout

```
coach-mcp/
├── CONTEXT.md
├── docs/{adr,example-session.md,build-order.md}
├── fixtures/garmin/         real scrubbed Garmin payloads, replayed by both seams
├── spikes/                  throwaway, committed for the record
├── src/
│   ├── Coach.Api/           MCP server, ingest endpoint
│   ├── Coach.Domain/        Program, Phase, Activity, ExerciseSet, Constraint
│   ├── Coach.Infrastructure/ persistence, FIT parsing
│   └── sync/                Python, runs at home
└── tests/
```
