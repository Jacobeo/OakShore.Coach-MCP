# The Garmin token file lives in the user profile, not the repository

Re-authenticating with Garmin needs an interactive MFA prompt roughly once a
year ([ADR 0001](0001-sync-runs-at-home.md)), so the token that login produces
has to survive between runs. Both the spike in spec 01 and the sync in spec 03
read the same token, and the sync runs unattended under Windows Task Scheduler,
which makes the location a shared interface rather than an implementation
detail of whichever script is written first.

**The token file is `%USERPROFILE%\.garminconnect\garmin_tokens.json`**, which
is `python-garminconnect`'s own default, reachable as `~/.garminconnect`.
`$GARMINTOKENS` overrides it, and every script accepts an explicit override
flag on top of that, so a second athlete or a test run can point elsewhere
without editing code.

Three things drove the choice:

- **It is outside the working tree.** A refresh token is a credential: anywhere
  under the repository it is one careless `git add -A` away from being
  published, and a `.gitignore` entry is a weaker guarantee than being on the
  other side of the filesystem.
- **It is per-user, and the scheduled task runs as that user.** The sync finds
  the token with no configuration, no service account and no secret store.
- **It is the library's default.** Anything else is a setting that has to be
  kept in step across the spike, the sync, and whatever runs them.

## Consequences

- Moving the sync to another machine means re-running the interactive login
  there. That is the intended behaviour, not a gap: the token never travels.
- The token is protected by the Windows account only — no additional
  encryption at rest. Acceptable for a single-athlete tool on a home machine,
  and the reason a cloud host was ruled out in the first place.
- Deleting `~/.garminconnect` is the supported way to force a fresh login when
  a token goes stale.
- Nothing in the repository ever needs to be scrubbed of credentials, because
  none were ever written there.
