# 01: Authenticated dump harness with Activity list

**What to build:** From the home machine, log in to the real Garmin account once
by hand — answering the MFA prompt — and have that login persist a token file
that every later run reuses without logging in again. In the same run, list
Activities over a recent window of a few weeks and write the raw, unparsed
response bytes to disk under a filename that names the endpoint.

The athlete can then run the script a second time, see that it does not prompt
for credentials, and open a file containing exactly what Garmin sent.

Pacing and the abort rule are in place from this first call, not retrofitted
later: at least a few seconds between calls, and on 401, 403 or 429 the script
stops immediately and reports what it got. It never retries, because retrying a
rate limit extends the block (ADR 0005). Nothing runs from Azure or from a
container on a cloud host (ADR 0001).

The token file's location is a decision, not an accident — the sync in spec 03
consumes the same token, so record where it lives and why.

Python, `python-garminconnect` 0.3.15 or later. This code is throwaway: it is
not the sync, it is not structured for reuse, and it should not be grown into
spec 03. Its durable output is the dumps.

**Blocked by:** None (can start immediately)

**Status:** done

- [x] A first interactive login from the home connection succeeds, MFA included
- [x] A token file is written, and a second run of the script reuses it without
      prompting for credentials or MFA
- [x] An Activity list over a recent window of a few weeks is fetched in one
      paced pass, paging included
- [x] The list response is on disk as raw bytes: no parsing, no transformation,
      no schema
- [x] Filenames identify the endpoint, and identify the Activity where a file
      belongs to one
- [x] At least a few seconds elapse between successive calls
- [x] On 401, 403 or 429 the script stops immediately and reports; it does not
      retry
- [x] The script makes no Garmin write of any kind
- [x] It is recorded whether the list response carries `totalSets`, `activeSets`
      and `totalReps`, so spec 03 knows whether it can skip a per-Activity call
- [x] The chosen token file location is written down, with the reason

## Outcome

Built in `spikes/strength-data-spike/`. Run against the real account on
2026-09-21 and again on 2026-09-22. The token lives at
`%USERPROFILE%\.garminconnect\garmin_tokens.json` per
[ADR 0006](../../../docs/adr/0006-garmin-token-file-location.md).

**The list carries the strength totals, and more.** A `strength_training`
Activity came back with `totalSets` 16, `activeSets` 16, `totalReps` 83 — and
with `summarizedExerciseSets` populated, holding real Exercise categories and
names (`BENCH_PRESS`, `TRAP_BAR_DEADLIFT`, `SEATED_CABLE_ROW`) and non-zero
`maxWeight` in grams. The per-Exercise sets and reps sum exactly to the two
totals. Spec 03 can therefore skip the per-Activity summary call for both the
totals and the per-Exercise aggregates. Whether per-`ExerciseSet` `weight` and
`repetitionCount` survive is issue 02's question; this is the summary, not the
sets.

Three things the run could not demonstrate, and how they are covered instead:

- **MFA was not demanded.** Garmin accepted the password alone. The prompt is
  wired and will fire when Garmin next asks.
- **Pacing was not observable.** Fourteen days held 3 Activities, so the pass
  was a single call with no second call to be paced behind. Covered by
  `test_dump_garmin.py`.
- **The abort rule was not triggered.** Also covered by `test_dump_garmin.py`,
  which is why that one test file exists despite the spec's no-tests line:
  provoking a 401, 403 or 429 by hand means provoking a real block.

**A finding for spec 03.** `python-garminconnect` 0.3.16 logs in through a
chain of five strategies. The first two returned 429 from Garmin's mobile
client IDs and the library carried on to a third, which succeeded. The abort
rule governs our own calls; it does not reach inside the library's login. The
mitigation is the token file — log in rarely.
