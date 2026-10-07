# Time in zones comes from the Activity list, against the boundaries observed

Spec 01's third issue dumped `hrTimeInZones` for eight cardio Activities and
the configured zone boundaries once. Reading those dumps changes the call shape
the sync was planned around.

**The seconds are already on the Activity list entry.** Each entry carries
`hrTimeInZone_1` through `hrTimeInZone_5`, and across all eight cardio
Activities in the 48-day window those values are identical to `secsInZone` from
the per-Activity endpoint. All fifteen Activities in the window carry all five
fields, none null — strength Activities included.

**The per-Activity call adds exactly one thing.** An `hrTimeInZones` row is
`{zoneNumber, secsInZone, zoneLowBoundary}`. The seconds are on the list entry
and the floors are in the boundaries response, so `zoneLowBoundary` is the only
field the call contributes, at one paced request per cardio Activity.

**What only the boundaries call has** is the context the floors need: which
`sport` profile they belong to, the `trainingMethod` they were derived from
(`HR_RESERVE` for this athlete), and `restingHeartRateUsed` and
`maxHeartRateUsed`. That last one matters because zone 5 has a floor and no
ceiling anywhere else — 167 upwards, closed only by the configured maximum of
180. That maximum is not a cap on observed data: one run in the window recorded
a `maxHR` of 183.

**So the sync reads time in zones off the Activity list, fetches the zone
boundaries once per run, and does not call `hrTimeInZones` at all.**

The boundaries are stored with the date they were observed, and a Cardio
Activity is measured against the set current when it is first stored with time in zones,
including the backfilled decade, which takes the set current at import. A
later change adds a revision beside the old set rather than overwriting it, and
an Activity keeps the revision it was first stored against, so a change never
re-scores Activities already stored. Zone history before the first observation
is not reconstructed. The athlete's judgement is that their zones do not move enough
for the distinction to be real, and the cost of being wrong is bounded: a zone
change shifts the scale of historical time-in-zone figures, it does not
invalidate the underlying Activities, and the seconds per zone as Garmin
recorded them are stored regardless.

## Consequences

- One fewer call per cardio Activity, permanently. For this athlete's eight
  cardio Activities in 48 days that is roughly sixty calls a year, against a
  budget [ADR 0005](0005-bounded-resumable-sync.md) sized for one enrichment
  call per Activity. That ADR's cost model narrows here: enrichment is one call
  per *strength* Activity, not per Activity.
- The backfill in [step 4](../build-order.md) is unchanged — it already
  computed time in zones from the FIT heart-rate series against the boundaries
  call, which is now the only zone source anywhere.
- Time in zones must be presented as computed against the zones observed on a
  stated date, not as the zones that applied at the time. Those are different
  claims and only the first is true.
- **Open for step 4:** recent Activities take Garmin's precomputed seconds off
  the list, while backfilled ones are computed from the FIT series. The two
  methods overlap where the export meets the sync window and can disagree on
  sampling and rounding. Measuring both against the same current revision
  removes scale from that disagreement but not method; which source wins on a collision is still to be
  decided.
- Read the zone fields defensively at the ingest boundary. An absent
  `hrTimeInZone_N` is unknown, never zero seconds, and the numbered fields
  should be read as they are found rather than assumed to stop at five — the
  list is fixed at five today, while the endpoint's array is variable length.
- This rests on one athlete's 48-day window, in which every Activity was
  recorded with a heart-rate strap. An Activity recorded without heart rate is
  unobserved, which is the case the defensive read above exists for.
