# The sync holds its own WorkOS sign-in

The sync posts to an ingest endpoint that validates a WorkOS access token's
signature, issuer and audience. Measured during the ticket 02 live smoke, those
access tokens expire five minutes after issue. A scheduled run therefore cannot
use a token pasted into the environment by hand: nobody is at the machine when
it runs, and a catch-up of many days outlives any one token even when somebody
is.

**The sync signs in to WorkOS once per machine, keeps the refresh token that
login produces, and issues its own access tokens from it for as long as it
needs them.** The sign-in is an authorization-code flow with PKCE against a
loopback redirect, run by hand with `--login`. Every later run is unattended.

Two alternatives were weighed.

A **WorkOS machine-to-machine client** is the better-shaped idea — a machine
identity with no human session behind it. It was not chosen because WorkOS
documents M2M applications as third-party only, carrying an `org_id` claim, and
does not document how such a token's `aud` is made to match the ingest
audience. Choosing it would have meant betting the ticket on an unverified
capability of a service tier this project may not have.

A **long-lived shared secret for `/ingest` alone** removes the lifetime problem
entirely and is the simplest thing to schedule. It was not chosen because it
adds a second authentication path to an endpoint carrying health data, and
because the one-issuer, one-audience story the rest of the system tells is
worth more than the few hours it would have saved.

## Consequences

- The refresh token is a credential and lives at
  `~/.coach-sync/workos_tokens.json`, in the user profile, for the reasons
  already set out for Garmin's token in
  [ADR 0006](0006-garmin-token-file-location.md). It never enters the working
  tree.
- WorkOS retires a refresh token as it is used, so each run writes its
  replacement back before using the access token it came with. A lost write
  costs an interactive sign-in, which is why that write is atomic and happens
  first.
- There are now two sign-ins that fall due independently — Garmin's roughly
  annual login and this one — renewed on different pages. They send different
  notifications, named for which one is due, so the athlete is never sent to
  the wrong place ([ADR 0001](0001-sync-runs-at-home.md)).
- A machine's sign-in does not travel. Moving the sync means signing in there,
  which is the intended behaviour rather than a gap.
- `COACH_ACCESS_TOKEN` still overrides everything with one static token. It is
  the escape hatch for a one-off manual run and for the smoke check, never for
  a scheduled one.
- Revoking the sync's access is done in WorkOS by revoking its client or
  session, without touching the athlete's own chat-client connection.
