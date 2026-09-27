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
dotnet run --project src/OakShore.Coach.Api --urls http://127.0.0.1:5180
```

| Variable | Value |
| --- | --- |
| `Authentication__Authority` | HTTPS URL of the hosted OIDC issuer |
| `Authentication__Audience` | Public MCP URL, ending in `/mcp`; must match the access token audience |
| `Server__PublicUrl` | Public server origin, without `/mcp`; used for authorization discovery |
| `Persistence__ConnectionString` | SQL Server connection string for a dedicated Coach database |
| `Ingest__BaseUrl` | Reachable URL of this API, e.g. `http://127.0.0.1:5180`; defaults to the public server origin |

The Infrastructure project applies its idempotent
[`0001_InitialAthleteProfile.sql`](src/OakShore.Coach.Infrastructure/Migrations/0001_InitialAthleteProfile.sql)
on startup. The database login needs schema-creation rights for this local
slice. The database must already exist. This first migration can be reapplied
after host recreation; future schema changes require their own migrations.

`/health` is anonymous and returns `{ "status": "ok" }` without calling MCP.
`/.well-known/oauth-protected-resource` publishes the configured resource and
issuer. Unauthenticated MCP and ingest requests return `401` with a
`WWW-Authenticate` challenge pointing to that metadata. CORS permits browser
origins using explicit bearer headers; cookie credentials are not enabled.

The MCP HTTP transport is explicitly stateless. Both initialization-based
clients and protocol `2026-07-28` clients can use it without a session ID. The
[C# SDK transport documentation](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/stateless/stateless.md)
describes the two protocol paths.

Hosted token issuance was exercised during deployment. Interactive ChatGPT and
Claude connector registration belongs to ticket 03. The build-order step 2
desktop/phone acceptance criterion therefore remains outstanding.

## Set up sign-in

The selected sign-in service is [WorkOS AuthKit](https://workos.com/docs/authkit/mcp).
It provides the login page and issues access tokens for the Coach MCP endpoint.
The Coach API validates each token's signature, issuer, and audience before it
reads or changes an AthleteProfile. No WorkOS API key is needed in the API.

1. Create a WorkOS account at <https://dashboard.workos.com> and select the
   **Staging** environment. Keep passwords, API keys, and billing details out
   of this repository and chat.
2. In the WorkOS dashboard, open **Connect → Configuration**. Enable **Client ID
   Metadata Document** and **Dynamic Client Registration**. The second setting
   supports MCP clients that do not yet use client metadata documents.
3. The Coach MCP URL is
   `https://oakshore-coach-api.yellowpond-6d7fb869.northeurope.azurecontainerapps.io/mcp`.
   It is registered in WorkOS Staging as the default **Resource Indicator**.
   The URL must exactly
   match `Authentication__Audience` in the deployed app. WorkOS puts that URL
   in the access token's `aud` claim.
4. Use `https://proficient-steam-61-staging.authkit.app` as
   `AuthenticationAuthority` for Staging deployment. Its
   `/.well-known/openid-configuration` document must return the same `issuer`
   and a working `jwks_uri`. The domain is public configuration, not a secret.
5. Sign in once through WorkOS, then open **Authentication → Features →
   Sign-up → Manage** and disable sign-up in Staging so that this personal
   project does not accept new users. Preserve the client registrations in
   **Connect** across app redeployments; deleting one breaks that client's
   existing connection.

[WorkOS staging is free](https://workos.com/docs/authkit/environments).
Production requires billing details, although AuthKit is free below its
published monthly active user allowance. A custom WorkOS domain is a paid
option and is unnecessary here. Verify these terms before switching to
Production. Staging is for setup and testing, not permanent live use.

## Deploy the body-weight slice

[Ticket 02](.scratch/02-athlete-profile-vertical-slice/issues/02-provision-and-deploy.md)
uses the two Bicep templates and PowerShell scripts in [`infra/`](infra/).
The target subscription needs permission to create resource groups, managed
identities, Azure SQL, and Container Apps, and to set a Microsoft Entra
administrator on the SQL server. Use Azure CLI, Bicep CLI, Docker with Linux
containers, and Windows PowerShell. The database grant uses a short-lived SQL
access token from the Azure CLI sign-in.

The chosen low-cost network design uses the SQL public endpoint and the Azure
services firewall rule. [Its reachability and cost trade-off](docs/hosting-cost-decision.md)
is explicit: the firewall accepts network attempts from other Azure
subscriptions, while Entra-only authentication limits database access to the
app identity and the server administrator. No private endpoint or customer VNet
is provisioned. The SQL database requests the free offer with `AutoPause` at
its monthly limit, and deployment stops if Azure does not report those settings.
Check the target subscription's free-offer eligibility and usage before running
the deployment; the templates do not create a paid database as a fallback.

Create a GitHub **personal access token (classic)** at
[Settings → Developer settings → Personal access tokens → Tokens (classic)](https://github.com/settings/tokens/new?scopes=write:packages).
Give it a descriptive name and an expiration date, select `write:packages`
(which also permits package downloads), and generate it. GitHub may select the
broader `repo` scope automatically; remove it if it is not needed for your
package. If the package belongs to an organization that requires SSO, authorize
the token for that organization. Copy the token once into the current PowerShell
process. It publishes the image and is supplied to Container Apps as a secret
for private GHCR pulls. Do not commit or paste it into command history:

```powershell
$secret = Read-Host 'GHCR token' -AsSecureString
$env:COACH_GHCR_TOKEN = [System.Net.NetworkCredential]::new('', $secret).Password
.\infra\publish-image.ps1 -GitHubUsername 'your-github-user'
```

Use the printed image digest and the same GitHub username. This project's
WorkOS Staging issuer is `https://proficient-steam-61-staging.authkit.app`;
its default OAuth resource is the app's `/mcp` URL:

```powershell
az login --scope https://management.core.windows.net//.default
.\infra\deploy.ps1 -AuthenticationAuthority 'https://proficient-steam-61-staging.authkit.app' `
  -Image 'ghcr.io/<github-user>/oakshore-coach-api@sha256:<digest>' `
  -RegistryUsername '<github-user>'
```

The GitHub token is needed before image publication. The Azure foundation was
deployed on 2026-09-23: the `coach` SQL database reports `useFreeLimit: true`
and `freeLimitExhaustionBehavior: AutoPause`; the Container Apps environment
and managed identity exist. The app identity has been granted database access.
The published image is deployed at
`https://oakshore-coach-api.yellowpond-6d7fb869.northeurope.azurecontainerapps.io`.
The health endpoint and OAuth metadata respond, and protected routes reject
unauthenticated requests. A WorkOS user token passed authenticated ingest,
MCP update, and MCP read checks. The 80.1 kg sample and its sync timestamp
survived a deployment with a new image digest.
Azure later reported the active revision at zero replicas; the next
authenticated read cold-started it and returned the same profile.

The script uses a separate resource group and a Container Apps environment in
North Europe by default. SQL uses West Europe because this subscription could
not create a new SQL server in North Europe on 2026-09-23. Traffic between the
app and SQL can incur cross-region transfer charges. The script
deploys the foundation, checks SQL's free offer and Entra-only authentication,
temporarily allows the deploying computer's IP to grant the app identity
database access, removes that firewall rule, and deploys the app. Its SQL
grant is repeatable. The app applies its idempotent schema migration on
startup. Re-running deployment with a new image digest updates the app without
replacing the database.

For the authenticated smoke check, set a valid bearer token for the deployed
audience in `COACH_ACCESS_TOKEN`. Choose body-weight values that are safe to
store for that token's athlete; the write check changes the actual profile:

```powershell
$secret = Read-Host 'Access token' -AsSecureString
$env:COACH_ACCESS_TOKEN = [System.Net.NetworkCredential]::new('', $secret).Password
.\infra\smoke.ps1 -AppUrl 'https://<app-host>' -WriteWeightKg 80.0 -ExpectedWeightKg 80.1
```

After a redeployment, run the smoke script with `-ExpectedWeightKg 80.1` and
omit `-WriteWeightKg`; it reads the saved profile without changing it. WorkOS
access tokens expire quickly, so use a fresh token for each smoke run.

The check requires 401 responses without a token, then verifies authenticated
ingest, MCP update, MCP read, and freshness. After publishing and deploying a
new image digest, run `smoke.ps1` again with `-ExpectedWeightKg` and no
`-WriteWeightKg` to verify stored data survived redeployment. Azure
Container Apps' replica metrics and a request after a quiet period provide
the scale-to-zero and cold-start evidence. Clear the two process variables
when finished.
With zero minimum replicas, an idle app can take more than 60 seconds to answer
its first request; retry after Azure reports a healthy running replica.

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

`OakShore.Coach.Domain` owns profile rules and repository/gateway interfaces and uses
only the BCL. `OakShore.Coach.Infrastructure` owns SQL Server mapping, persistence, and
the ingest HTTP gateway. `OakShore.Coach.Api` owns authentication, endpoint/tool mapping,
and composition in `Program.cs`. Tests assert these dependency directions with
NetArchTest. The Python spike is independent of this slice.
