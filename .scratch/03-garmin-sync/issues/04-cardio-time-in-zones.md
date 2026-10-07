# 04: Cardio Activities with time in zones

**What to build:** Cardio Activities carry the time spent in each HeartRateZone
together with the boundaries that were in force, so intensity distribution can be
analysed and a ride is never compared against a run's zones.

**Blocked by:** 01 Activities into the store, safely.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0008](../../../docs/adr/0008-one-heart-rate-zone-set.md).

- [x] Time in each zone is read from the numbered fields on the Activity list
  entry. The per-Activity zones endpoint is not called.
- [x] Zone boundaries are fetched once per run, not once per Activity, and are
  stored per SportProfile with each zone's number and lower boundary.
- [x] Each Cardio Activity records the boundaries in force when it happened, so a
  later boundary change does not retroactively alter it.
- [x] An Activity whose sport has no override resolves against the athlete's
  default SportProfile.
- [x] The Python seam asserts one boundary fetch per run regardless of Activity
  count; the .NET seam asserts the stored zone times and their SportProfile.

The deferred intensity dimension of Adherence
([spec 04](../../04-macrocycle-and-prescription.md)) consumes this data when it
is built; nothing extra is captured for it.

## Comments

2026-10-07: Implemented. The ticket's third criterion contradicted ADR 0008's
"one set, no zone history"; the athlete chose dated revisions, and the ADR is
amended in the same change. A SportProfile whose delivered boundaries differ from
its latest stored ones gains a revision observed at the batch's acceptance time;
a Cardio Activity is pinned to the current revision of its resolved SportProfile
the first time it is stored with time in zones, and re-syncs never re-pin it.
Judgment calls, each open to correction: the sync fetches the boundaries lazily,
at most once per run and only when an Activity in the run carries time in zones,
so a strength-only or empty run costs no extra call. Resolution lives in the
domain and maps `typeKey` to Garmin's `RUNNING`, `CYCLING` and `SWIMMING`
overrides by name pattern (running/`_run`; cycling/biking/`virtual_ride`;
swimming). The corpus holds only `DEFAULT`, so those override names are
unverified against a live response. Strength Activities carry no time in zones
even though the list entry has the fields — the ticket scopes this to Cardio and
nothing downstream asks for it. `get_sync_status` gained a `SportProfile`
collection and `latestActivityCardioDetail`; CONTEXT.md gained CardioDetail.
After review: an empty `timeInHeartRateZones` clears the seconds but keeps the
pin, so a later re-send is not re-pinned; a changed `typeKey` is the one case
that re-pins, to its new sport's current revision. The sync stops with a clear
error when the boundaries carry no DEFAULT with floors, rather than posting a
batch ingest would reject. Left open: `restingHrAutoUpdateUsed` and
`changeState` are not captured; ingest accepts time in zones for any `typeKey`
(the sync sends it for Cardio only); a boundary change that is a resting heart
rate change alone also adds a revision.
