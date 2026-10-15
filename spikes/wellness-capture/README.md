# Wellness capture

Throwaway capture for tickets 05 and 06 of
[spec 03](../../.scratch/03-garmin-sync.md). It fetches Garmin's per-date
endpoints once, paced and read-only, so their real responses can be read and
replayed, and so what this athlete's watch produces is known before a ticket is
planned around it. Its durable output is the wellness and daily form parts of
[`fixtures/garmin/`](../../fixtures/garmin/README.md) and three ADRs:
[0010](../../docs/adr/0010-wellness-reads-sleep-and-the-daily-summary.md),
[0011](../../docs/adr/0011-this-watch-records-no-hrv.md) and
[0012](../../docs/adr/0012-daily-form-is-one-training-status-call.md). Never imported.

## Running it

From the home machine, over the residential connection
([ADR 0001](../../docs/adr/0001-sync-runs-at-home.md)), with the Garmin token
file the sync uses:

```powershell
python capture_wellness.py --plan wellness
python scrub_wellness.py dumps/<run> ../../fixtures/garmin
```

The token is loaded through the sync's own `load_token_client`, so a token due
for refresh is refreshed first, paced like every other request. If the refresh
fails the capture stops: a lapsed refresh token needs the interactive login, by
hand. Any status other than 200 or 204 stops the run without retrying
([ADR 0005](../../docs/adr/0005-bounded-resumable-sync.md)).

| Plan | Calls | |
| --- | --- | --- |
| `wellness` | 15 | social profile; sleep, HRV and daily summary for three days and a 2010-01-01 sentinel; dedicated body battery and stress for one day |
| `form` | 4 | training status, TrainingReadiness and VO2max for one day; the latest cycling FTP |
| `training-status` | 4 | training status for the same three days and the 2010-01-01 sentinel |

Raw responses land in the gitignored `dumps/`, one directory per run. Only
`scrub_wellness.py` moves anything into the corpus, and only from a `wellness`
or `training-status` run; `python -m unittest test_scrub_wellness` covers its
refusals.

## When to run it again

When the athlete changes watch. ADR 0011 rests on this watch recording no
HRV, and the probe on ticket 06 on it producing no TrainingReadiness; a new
watch reopens both.
