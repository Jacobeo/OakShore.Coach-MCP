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

**Status:** ready-for-agent

- [ ] A first interactive login from the home connection succeeds, MFA included
- [ ] A token file is written, and a second run of the script reuses it without
      prompting for credentials or MFA
- [ ] An Activity list over a recent window of a few weeks is fetched in one
      paced pass, paging included
- [ ] The list response is on disk as raw bytes: no parsing, no transformation,
      no schema
- [ ] Filenames identify the endpoint, and identify the Activity where a file
      belongs to one
- [ ] At least a few seconds elapse between successive calls
- [ ] On 401, 403 or 429 the script stops immediately and reports; it does not
      retry
- [ ] The script makes no Garmin write of any kind
- [ ] It is recorded whether the list response carries `totalSets`, `activeSets`
      and `totalReps`, so spec 03 knows whether it can skip a per-Activity call
- [ ] The chosen token file location is written down, with the reason
