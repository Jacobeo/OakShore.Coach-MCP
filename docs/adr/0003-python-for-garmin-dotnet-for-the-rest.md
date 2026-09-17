# Python talks to Garmin; everything else is .NET

The project is otherwise .NET, so a Python component needs justification.

Garmin fronts its auth endpoints with Cloudflare TLS fingerprinting. Defeating
it requires a client that impersonates a real browser's TLS signature. The
maintained Python client does this via `curl_cffi`; the .NET libraries use plain
`HttpClient` and are consequently blocked. The one .NET library attempting
impersonation is pre-1.0 and barely used.

So the sync is Python, and the MCP server, datastore, analytics and protocol
work are .NET. The two communicate over an authenticated ingest endpoint, never
a shared database connection.

## Consequences

- Two runtimes to install on the home sync machine.
- The seam is a versioned HTTP contract, which is a reasonable boundary anyway.
- If the .NET ecosystem catches up on TLS impersonation, the Python side can be
  replaced without touching anything else.
- Garmin's official .NET FIT SDK is still used for parsing `.fit` files; that
  part never needed Python.
