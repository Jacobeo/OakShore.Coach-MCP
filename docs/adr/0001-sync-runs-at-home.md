# Garmin sync runs from home, not from Azure

Garmin has no self-serve API for individuals, so all access is through an
unofficial client that Garmin actively defends against: login is rate-limited by
**IP address**, blocks persist for hours or days, and datacenter IP ranges fare
measurably worse than residential ones. Re-authentication also needs an
interactive MFA prompt roughly once a year.

We therefore run the Garmin-facing sync on a machine at home, over a residential
connection, and push the results to Azure. The hosted server never contacts
Garmin; it only reads our own datastore.

## Consequences

- Garmin only ever sees residential traffic, and the annual interactive login
  happens on a machine with a human sitting at it.
- Garmin credentials and the auth token file never leave the house.
- A Garmin outage, block, or auth break degrades to **stale data**, not to a dead
  service.
- The hosted server is stateless and read-only, which is exactly the shape that
  runs for free on scale-to-zero container hosting.
- Every additional athlete needs their own home sync. This effectively rules out
  onboarding a non-technical user, and is accepted.
