# 03: Heart-rate zone dumps for cardio Activities

**What to build:** For the non-strength Activities in the listed window, fetch
the heart-rate-time-in-zones response and dump it raw. At minimum a run and a
bike Activity, so the cardio path is validated at the same time as the strength
path and is not left as a second unknown.

Separately, fetch the configured heart-rate zone boundaries once. The boundaries
are per sport profile, and cross-sport comparison built on a single wrong set of
zones would be silently wrong, so the dump needs to show which profile each set
of boundaries belongs to.

Reuse the harness from ticket 01 as-is: same token, same pacing, same abort on
401/403/429. Read-only throughout.

This ticket does not depend on ticket 02 and can run alongside it.

**Blocked by:** 01 (Authenticated dump harness with Activity list)

**Status:** ready-for-agent

- [ ] A run Activity's `hrTimeInZones` response is on disk as raw bytes
- [ ] A bike Activity's `hrTimeInZones` response is on disk as raw bytes
- [ ] The zone-boundaries call is made exactly once and dumped raw
- [ ] The dumps show zone boundaries per sport profile, not a single
      undifferentiated set
- [ ] Filenames tie each dump to its Activity and endpoint
- [ ] No writes, and the pacing plus no-retry abort from ticket 01 still hold
