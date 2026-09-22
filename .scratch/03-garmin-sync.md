# Spec 03 — Garmin sync

## Problem Statement

The datastore is the system of record (ADR 0002), but nothing fills it. The
athlete's training history lives in Garmin Connect, which offers individuals no
sanctioned API: the developer programme is enterprise-only and new access
requests have been paused since spring 2026.

The only practical route is an unofficial client, and Garmin actively defends
against it. Logins are rate-limited by IP address, blocks persist for hours or
days, datacenter addresses fare measurably worse than residential ones, and the
authentication flow was broken outright in March 2026 by TLS fingerprinting. The
maintained Python client defeats this by impersonating a browser's TLS signature;
no .NET library does (ADR 0003).

Beyond access, the shape of the data creates its own hazard. Sleep, HRV, body
battery, stress, daily summaries, training status and training readiness are all
per-date endpoints, at roughly six calls for every day synced. The athlete's
machine is typically on only for a few hours each evening, so gaps of several
days are routine and gaps of a fortnight are expected. A naive catch-up would
issue its largest burst of requests at precisely the moment it is most likely to
be blocked.

Finally, a decade of history cannot be fetched this way at all: per-day endpoints
would require tens of thousands of calls.

## Solution

A Python sync that runs on the home machine, driven by a watermark rather than a
schedule, doing a bounded and paced amount of work per run, and posting the
results to the ingest endpoint built in spec 02. It never contacts Azure's
database directly and Azure never contacts Garmin.

Multi-year history is loaded separately from Garmin's own bulk data export, which
is sanctioned, free, and costs no API calls at all. The API is therefore only ever
used for the recent window.

Every MCP response exposes how stale its data is, so that the agent can qualify
its answers rather than reasoning confidently over a gap.

## User Stories

1. As the athlete, I want my completed Activities to appear in the coach without my doing anything, so that the data is simply there when I ask a question.
2. As the athlete, I want the sync to run each evening while my machine is on, so that it fits the hours I actually use my computer.
3. As the athlete, I want a sync to catch up automatically when my machine has been off for several days, so that missed nights heal themselves.
4. As the athlete, I want a single catch-up run after several missed days rather than one run per missed day, so that a holiday does not cause a burst of traffic.
5. As the athlete, I want to trigger a sync by hand when I have just finished a session, so that I can ask about it immediately.
6. As the athlete, I want a manual run in the middle of the day to be harmless, so that I never have to think about whether it is safe.
7. As the athlete, I want the sync to pause between requests, so that catching up never looks like scraping.
8. As the athlete, I want the sync to stop immediately if Garmin rate-limits or challenges it, so that retrying does not extend a block.
9. As the athlete, I want a notification on my phone when a sync fails permanently, so that I find out without checking logs.
10. As the athlete, I want transient network failures retried silently, so that I am not notified about problems that fixed themselves.
11. As the athlete, I want the agent to know when it was last synced, so that it can tell me it might be missing recent sessions.
12. As the athlete, I want my Garmin credentials to stay on my own machine, so that a cloud breach cannot expose them.
13. As the athlete, I want to log in interactively once and not again for about a year, so that the sync runs unattended.
14. As the athlete, I want a clear notification when the annual re-authentication is required, so that I know to sit down and complete it.
15. As the athlete, I want my strength Activities stored with per-ExerciseSet detail, so that I can ask about specific lifts over time.
16. As the athlete, I want my cardio Activities stored with time spent in each heart-rate zone, so that intensity distribution can be analysed.
17. As the athlete, I want my configured zone boundaries stored per sport, so that a bike session and a run are not compared against the same zones.
18. As the athlete, I want sleep, HRV, body battery, stress and daily summaries stored, so that recovery can be related to training.
19. As the athlete, I want training status, load, readiness and VO2max stored, so that current form can be assessed.
20. As the athlete, I want years of history loaded from Garmin's official export, so that long-horizon questions are answerable.
21. As the athlete, I want the export ingested without thousands of API calls, so that backfill carries no blocking risk.
22. As the athlete, I want an interrupted sync to resume where it stopped, so that no work is repeated and none is lost.
23. As the athlete, I want re-running a sync over a period already synced to change nothing, so that I can run it freely.
24. As the athlete, I want the most recent days synced first after a long gap, so that the data I actually ask about arrives first.
25. As a developer, I want the sync to post the same versioned payloads the tests replay, so that the contract is exercised in both directions.
26. As a developer, I want captured Garmin responses replayed in tests, so that the sync is testable without touching a live account.

## Implementation Decisions

**Placement and runtime.** Python, on the home machine, over the residential
connection, per ADR 0001. Scheduled with the operating system's task scheduler,
configured to run a missed occurrence as soon as the machine is next available.
Note that missed occurrences are coalesced into a single run — the design does not
rely on this, but it is the reason a catch-up cannot multiply.

**A watermark, not a schedule.** The sync reads the last date successfully synced
and works forward. Each day is committed independently, so an interruption keeps
everything already fetched. This makes the sync idempotent and makes scheduler
behaviour irrelevant.

**Bounded work per run.** A hard ceiling on the number of wellness-days fetched in
one run, working most-recent-first, then stopping with the watermark where it
reached. A long gap heals over several nights. Recent data, which is what the
coach reasons over, arrives on the first run. Per ADR 0005.

**Pacing and circuit breaker.** A few seconds between per-date calls. On 401, 403
or 429 the run aborts immediately and notifies; it never retries, because
retrying a rate limit extends the block, and a 403 specifically indicates a bot
challenge. Retries with backoff apply only to network errors and 5xx.

**Enrichment happens at ingest time, never at query time.** A chat response must
never depend on a live Garmin call, which could be slow, rate-limited or blocked.
Whatever the sync does not capture is expensive to obtain later, so it captures
everything on the way in.

**Call shape.** The Activity list covers an arbitrary date range in one call and
already carries strength set and repetition totals, so the per-Activity summary
call is not used. It also carries `hrTimeInZone_1` through `hrTimeInZone_5`, so
the per-Activity time-in-zones call is not used either
([ADR 0008](../docs/adr/0008-one-heart-rate-zone-set.md)). Enrichment is one
further call per *strength* Activity, for exercise sets. Zone boundaries are
fetched once per run, and the response carries one entry per configured sport
profile. Cost therefore scales with days of wellness data, not with the number
of Activities.

**Known payload hazards**, each of which costs an hour if discovered late: the
per-Activity zones response is an unordered list that must be sorted by zone
number, which is one more reason to read the list entry's explicitly numbered
`hrTimeInZone_N` fields instead; weight
appears in grams in the exercise-sets JSON but in a scaled kilogram unit in FIT
files, so a canonical unit must be established at the ingest boundary; activity
detail samples are positional and must be zipped onto their metric descriptors;
there is no pace channel and pace must be derived from speed; and the calendar
endpoint takes a zero-indexed month.

**Writes are forbidden** in this spec, and the exercise-sets write endpoint is
forbidden permanently, per ADR 0004.

**Bulk history.** Garmin's official export is requested by hand and arrives by
email within about two days, though Garmin's own documentation allows up to
thirty. Its archive contains activity summaries with per-Exercise aggregates,
sleep, daily rollups, training readiness and raw FIT files, but no planned
workouts and no training plans — those are API-only. Structure varies between
exports, files are chunked by date range, and nested archives hold the FIT files,
so the importer must glob rather than assume filenames and must treat every field
as optional. Per-set detail for historic Activities comes from parsing FIT set
messages using Garmin's official .NET FIT SDK, which is why that dependency
exists.

**Failure notification.** A push notification from the home machine on final
failure, using a free topic-based service. It must originate at home, not in
Azure, because Azure cannot distinguish a failed sync from a machine that is
switched off. The agent-facing signal is separate: the freshness timestamp
already carried by every response.

## Testing Decisions

Tests assert on what crosses a boundary, not on how the code is arranged. For the
sync, the observable behaviour is the sequence of requests it makes and the
batches it posts.

**The Python seam — the Garmin client boundary.** Real responses captured by spec
01 are replayed as fixtures. The sync under test is asserted on the batches it
posts to ingest. No live Garmin account is involved in any automated test.

**Behaviours covered at that seam.** Watermark advancement; resumption after an
interruption; idempotence when re-run over an already-synced period; the per-run
ceiling being respected; most-recent-first ordering; pacing between calls; abort
without retry on 401, 403 and 429; retry with backoff on network errors and 5xx;
correct classification of strength versus cardio Activities for enrichment; and
notification on final failure.

**The .NET side is covered by the existing seam from spec 02** — the ingest
endpoint. Batch validation, unit normalisation, idempotent upsert behaviour and
the freshness timestamp are asserted by posting fixtures and reading the result
back through MCP tools. No new .NET seam is introduced.

**Export ingestion** is tested at the same ingest seam, using a trimmed sample
archive containing a handful of activities and one nested FIT file. Structural
variation between real exports is covered by asserting that a missing optional
field is tolerated rather than fatal.

**Prior art.** Spec 02 established the .NET seam and this spec reuses it. The
Python seam is new and is established here.

## Out of Scope

Any write to Garmin. The Program, Phases, prescription and Adherence. Per-second
metric series beyond what zone analysis requires. Real-time or intraday sync.
Supporting more than one athlete's sync on one machine. Automating the bulk
export request, which Garmin provides only through its account pages.

## Further Notes

The realistic cost of a gap is smaller than it feels. Two weeks is roughly eight
minutes of paced syncing; a month is about fifteen. The bounded-per-run ceiling
exists for the pathological cases — a long holiday, or a first run before the
export has been ingested — rather than for ordinary use.

The unofficial client conflicts with Garmin's terms of use, whose stated remedy
is account suspension. No account bans are known to the library's maintainer, and
the mitigations here — residential traffic only, one login a year, paced and
bounded requests, sanctioned export for bulk history — are chosen to keep the
footprint as small as the use case allows. This is an accepted risk, not an
overlooked one.
