# 04: Cardio Activities with time in zones

**What to build:** Cardio Activities carry the time spent in each HeartRateZone
together with the boundaries that were in force, so intensity distribution can be
analysed and a ride is never compared against a run's zones.

**Blocked by:** 01 Activities into the store, safely.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0008](../../../docs/adr/0008-one-heart-rate-zone-set.md).

- [ ] Time in each zone is read from the numbered fields on the Activity list
  entry. The per-Activity zones endpoint is not called.
- [ ] Zone boundaries are fetched once per run, not once per Activity, and are
  stored per SportProfile with each zone's number and lower boundary.
- [ ] Each Cardio Activity records the boundaries in force when it happened, so a
  later boundary change does not retroactively alter it.
- [ ] An Activity whose sport has no override resolves against the athlete's
  default SportProfile.
- [ ] The Python seam asserts one boundary fetch per run regardless of Activity
  count; the .NET seam asserts the stored zone times and their SportProfile.
