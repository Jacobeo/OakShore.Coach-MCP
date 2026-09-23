# Coach MCP

The local body-weight slice exposes `get_athlete_profile` and
`update_athlete_profile` at `/mcp`, with authenticated writes through `/ingest`.
It implements [ticket 01](.scratch/02-athlete-profile-vertical-slice/issues/01-body-weight-through-mcp.md).

## Verify locally

Install a .NET SDK that supports .NET 8, the .NET 8 ASP.NET runtime, and Docker
with Linux containers. From the repository root:

```powershell
dotnet test coach-mcp.sln
```

Tests start and remove a real SQL Server 2022 container. The first run downloads
the image. No Garmin account, external identity provider, or existing database
is needed. Tests use signed JWTs with a local issuer key; the production bearer
validator still checks the signature, issuer, audience, expiry, and subject.
Only the issuer's discovery metadata and the HTTP transport into the in-process
test host are supplied by the harness. Repositories and SQL Server are real.

## Run the API

Set these environment variables before running the API:

```powershell
dotnet run --project src/Coach.Api --urls http://127.0.0.1:5180
```

| Variable | Value |
| --- | --- |
| `Authentication__Authority` | HTTPS URL of the hosted OIDC issuer |
| `Authentication__Audience` | Public server URL; must match the access token audience |
| `Persistence__ConnectionString` | SQL Server connection string for a dedicated Coach database |
| `Ingest__BaseUrl` | Reachable URL of this API, e.g. `http://127.0.0.1:5180`; defaults to the audience |

The Infrastructure project creates the initial schema on startup with EF Core
`EnsureCreated`. The database login needs schema-creation rights for this local
slice. This is an initial-schema bootstrap, not a migration runner for an
existing schema.

`/health` is anonymous and returns `{ "status": "ok" }` without calling MCP.
`/.well-known/oauth-protected-resource` publishes the configured resource and
issuer. Unauthenticated MCP and ingest requests return `401` with a
`WWW-Authenticate` challenge pointing to that metadata. CORS permits browser
origins using explicit bearer headers; cookie credentials are not enabled.

The MCP HTTP transport is explicitly stateless. Both initialization-based
clients and protocol `2026-07-28` clients can use it without a session ID. The
[C# SDK transport documentation](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/stateless/stateless.md)
describes the two protocol paths.

Hosted token issuance and real ChatGPT/Claude connections belong to ticket 03;
deployment belongs to ticket 02. The build-order step 2 desktop/phone acceptance
criterion therefore remains outstanding.

## Ingest version 1

Send an access token for the same audience and issuer as MCP:

```http
POST /ingest
Authorization: Bearer <access-token>
Content-Type: application/json

{"version":1,"athleteProfiles":[{"bodyWeightKg":82.5}]}
```

`sub` determines `UserId`; neither endpoint accepts a caller-selected athlete.
The subject must be nonblank, at most 255 characters, and have no surrounding
whitespace. Database comparisons are case-sensitive. Unexpected ingest fields,
including supplied `userId` fields, are rejected.

Implementation choices for this first contract:

- Body weight is positive kilograms, with at most one decimal place. Values
  must fit `decimal(18,1)` (less than 10^17 kg); there is no medical range rule.
- A batch has 1–100 changes for the authenticated athlete. All changes validate
  before writing. Changes apply in list order; the final value becomes the
  current AthleteProfile in one transaction. Unsupported versions and invalid
  batches leave both the value and freshness unchanged.
- `lastSyncedAt` is the server's UTC acceptance time for the saved batch.
  An empty store returns `{"profile":null,"lastSyncedAt":null}`. A saved result
  carries `profile.userId`, `profile.bodyWeightKg`, and `lastSyncedAt`.
- MCP success responses have `structuredContent` and an empty `content` array.
  Update returns the saved facts. Validation errors report no data freshness;
  protocol/binding errors use the SDK's error format.

The update tool sends its batch over HTTP to `/ingest`, forwarding the current
bearer token. The target URL comes from configuration, never the incoming Host
header; redirects are disabled. A boundary test denies ingest authorization
and verifies that the tool cannot persist a value through another route.

## Project boundaries

`Coach.Domain` owns profile rules and repository/gateway interfaces and uses
only the BCL. `Coach.Infrastructure` owns SQL Server mapping, persistence, and
the ingest HTTP gateway. `Coach.Api` owns authentication, endpoint/tool mapping,
and composition in `Program.cs`. Tests assert these dependency directions with
NetArchTest. The Python spike is independent of this slice.
