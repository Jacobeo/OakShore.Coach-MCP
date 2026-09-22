# 02: ExerciseSet dumps for strength Activities

**What to build:** For every strength Activity in the listed window, fetch the
exercise-sets response and dump it raw to disk. The athlete ends up with real
`exerciseSets` payloads from their own sessions, and can read field names and
units off the wire rather than off documentation.

Two cases matter and they must be distinguishable in the output: a session where
the athlete followed a Workout pushed to the watch, and a freestyle session if
any exist in the window. Research says watch-recorded strength data is often
inferred from the accelerometer with `weight` null and category `UNKNOWN`, while
a followed Workout is the best case for Garmin's data model. Whether that split
shows up in this athlete's history is the whole question, so make the difference
visible rather than assumed — if the window contains only one of the two cases,
say so explicitly instead of leaving it ambiguous.

Reuse the harness from ticket 01 as-is: same token, same pacing, same abort on
401/403/429.

Read-only, and specifically: the exercise-sets **write** endpoint is never
called. It has replace-all semantics over recorded history and a malformed
payload destroys training data that cannot be recovered from Garmin (ADR 0004).

The per-Activity summary call is deliberately not used — the list response from
ticket 01 already carries the strength totals.

**Blocked by:** 01 (Authenticated dump harness with Activity list)

**Status:** awaiting-live-run

- [ ] Every strength Activity in the window has its exercise-sets response on
      disk as raw bytes
- [ ] At least one dumped Activity comes from a session that followed a pushed
      Workout
- [x] A freestyle session is dumped if one exists in the window; if none does,
      that is recorded rather than left unstated
- [x] Filenames tie each dump to its Activity and endpoint
- [x] `set_activity_exercise_sets`, and every other write endpoint, is never
      called
- [x] The per-Activity summary endpoint is not called
- [x] Pacing and the no-retry abort from ticket 01 still hold across the whole
      per-Activity pass

## Outcome

The per-Activity pass is in `dump_garmin.py`: one paced `GET` of
`/activity-service/activity/{id}/exerciseSets` per strength Activity, on the
same `PacedReader` as the list pass, dumped verbatim to
`activity-service--exerciseSets__activity-<id>.json` in the run directory and
recorded in its `index.json`.

**No freestyle strength Activity exists in this athlete's recent history.** All
seven strength Activities in the 2026-08-05 to 2026-09-22 window carry a
`workoutId`, so every one of them followed a Workout pushed to the watch. The
dumps can therefore only show the best case for Garmin's data model; the
degraded case research warns about is unobserved rather than absent, and a
wider `--days` is the only way to look for one. That is stated in
`spikes/strength-data-spike/exercise-set-coverage.md`, which is regenerated
every run and also by `--from-dumps` with no Garmin call. Which dump came from
which case is tied to individual Activities in `exercise-set-cases.md`, written
into the run directory because it names them.

**The two remaining boxes need a live run from the home machine**, which the
athlete makes rather than an agent:

```
.venv/Scripts/python.exe dump_garmin.py --days 48
```

Forty-eight days is the window the committed dump covers, and `--days 48`
reproduces it: eight paced calls, one list call and seven `exerciseSets` calls.
The default fourteen days holds only one strength Activity, and running it would
narrow the coverage note to that one — the seven-Activity finding above is a
claim about the wider window and needs the wider window to stay true.

Everything up to the live run was verified offline: the pass replayed against
the committed list dump through a fake session, which showed the filenames, the
index entries and that the only URLs issued are `GET`s of `exerciseSets`.

Enforcement of the three "never called" criteria is structural, not incidental:
`PacedReader` can issue nothing but `GET`, and `ACTIVITY_LIST_PATH` and
`EXERCISE_SETS_PATH` are the only paths the script can ask for, so neither
`set_activity_exercise_sets` nor the per-Activity summary endpoint is
reachable. `test_dump_garmin.py` covers GET-only, pacing between Activities,
and that an abort mid-pass leaves the remaining Activities unfetched.
