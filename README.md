# Coach MCP

The local AthleteProfile slice exposes `get_athlete_profile` and
`update_athlete_profile` at `/mcp`, with authenticated writes through `/ingest`.
It implements [ticket 01](.scratch/02-athlete-profile-vertical-slice/issues/01-body-weight-through-mcp.md),
[ticket 04](.scratch/02-athlete-profile-vertical-slice/issues/04-stable-facts-and-goal.md),
and [ticket 05](.scratch/02-athlete-profile-vertical-slice/issues/05-temporary-constraints.md).

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

The Infrastructure project applies its idempotent AthleteProfile migrations
on startup. The database login needs schema-creation rights for this local
slice. The database must already exist. The migrations preserve existing body
weights and convert a saved single Goal into the first prioritized Goal.

`/health` is anonymous and returns `{ "status": "ok" }` without calling MCP.
`/.well-known/oauth-protected-resource` publishes the configured resource and
issuer. Unauthenticated MCP and ingest requests return `401` with a
`WWW-Authenticate` challenge pointing to that metadata. CORS permits browser
origins using explicit bearer headers; cookie credentials are not enabled.

The MCP HTTP transport is explicitly stateless. Both initialization-based
clients and protocol `2026-07-28` clients can use it without a session ID. The
[C# SDK transport documentation](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/stateless/stateless.md)
describes the two protocol paths.

Hosted token issuance was exercised during deployment. ChatGPT connected
through WorkOS Staging on desktop and phone on 2026-09-27, and read a persisted
body-weight update with freshness on both. The athlete reported no repeat
sign-in or per-read approval prompt. A ChatGPT read from zero Azure replicas
returned the saved value in about 42 seconds without either prompt. Claude
acceptance remains separate from this ChatGPT pass.

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

## Connect ChatGPT

Use the athlete's ChatGPT account on desktop. In **Settings → Security and login**,
enable **Developer mode**. Open **ChatGPT Plugins**, add a connection named
**Coach**, and enter
`https://oakshore-coach-api.yellowpond-6d7fb869.northeurope.azurecontainerapps.io/mcp`
as its server URL. ChatGPT's [MCP setup guide](https://developers.openai.com/plugins/build/app-quickstart)
describes this flow. The connector should expose exactly
`get_athlete_profile` and `update_athlete_profile`.

The WorkOS Staging issuer advertises Client ID Metadata Document support,
Dynamic Client Registration, and PKCE `S256`. Its OAuth metadata does not
advertise issuer identification, so current
[OpenAI authentication guidance](https://developers.openai.com/plugins/build/auth)
calls for a callback-specific redirect URI. If a future connection setup asks
for a redirect URI, use the one shown for that connection; do not substitute
the stable callback URL from older instructions. If the connection uses
Dynamic Client Registration, preserve its registered client and credentials
in WorkOS across Coach redeployments. Deleting that registration breaks the
existing ChatGPT connection.

The athlete's ChatGPT management page shows OAuth but does not expose which
registration method or callback it used. No callback URI was entered manually
while connecting Coach on 2026-09-27. Record those details from ChatGPT or
WorkOS if either later makes them available; do not infer them from a working
sign-in.

After linking through WorkOS, verify the connection in a desktop conversation:

1. Ask "What body weight do you have saved, and when was it last synced?" The
   answer should come from `get_athlete_profile` and include `lastSyncedAt`.
   Reading should not require per-call approval.
2. Tell ChatGPT a body weight that the athlete actually wants saved. Check that
   it calls `update_athlete_profile`, confirms the saved value in words, and
   returns a freshness timestamp. Read again in a later turn.
3. Start a new conversation and read again. Confirm that WorkOS does not ask for
   another sign-in. Open ChatGPT on the athlete's phone, select the same
   connection, and repeat the read there. Record the date, client surface,
   observed value and freshness, and any sign-in or approval prompt in
   [ticket 03](.scratch/02-athlete-profile-vertical-slice/issues/03-connect-chatgpt-and-claude.md).

The app scales to zero, so the first request after an idle period may take over
a minute. If the first attempt times out, retry after the revision is healthy
and record both attempts; a successful retry alone does not prove ChatGPT's
cold-start behavior is acceptable. Refresh the ChatGPT connection after any
change to its tools or metadata.

If Coach is selectable in ChatGPT but its details say no tools are available,
open Coach's connected account and choose **Reconnect**. Check that both
profile tools appear before testing a conversation.

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

{"version":1,"athleteProfiles":[{"bodyWeightKg":82.5,"availableEquipment":["Full gym","Stationary bike"],"intendedTrainingFrequencyPerWeek":3,"intendedTrainingDurationMinutes":60,"lastingLimitations":["Weak ankles"],"goals":[{"description":"Play 7-a-side football"},{"description":"Improve VO2max"}],"goalsTargetDate":"2099-04-01"}]}
```

`sub` determines `UserId`; neither endpoint accepts a caller-selected athlete.
The subject must be nonblank, at most 255 characters, and have no surrounding
whitespace. Database comparisons are case-sensitive. Unexpected ingest fields,
including supplied `userId` fields, are rejected.

Contract rules:

- Body weight is positive kilograms, with at most one decimal place. Values
  must fit `decimal(18,1)` (less than 10^17 kg); there is no medical range rule.
- `availableEquipment` and `lastingLimitations` are free-text lists with at most
  50 entries. Each entry has 1–200 nonblank characters and no surrounding
  whitespace. An empty list clears that list. Lasting limitations are stable
  facts; temporary Constraints are separate.
- `intendedTrainingFrequencyPerWeek` is an integer from 1 to 21 times per week.
  `intendedTrainingDurationMinutes` is an integer from 1 to 1440 minutes.
- `goals` is an ordered list, highest priority first, with at most 20 distinct
  descriptions of 1–500 nonblank characters without surrounding whitespace.
  Duplicate descriptions are rejected without regard to case. An empty list
  clears all Goals and their shared date.
- `goalsTargetDate` is one shared ISO `YYYY-MM-DD` date, today or later in UTC
  when saved. It is the intended end date of the future Macrocycle; individual
  Goals have no dates. Existing version 1 ingest payloads with a single `goal`
  object remain accepted and become the first Goal plus the shared date. A
  payload cannot supply both `goal` and `goals` or `goalsTargetDate`.
- `constraint` appends one temporary Constraint per change. Its `description`
  has 1–500 nonblank characters without surrounding whitespace. `validFrom`
  and `validUntil` are UTC timestamps ending in `Z`, such as
  `2030-04-10T00:00:00Z`. Fractional seconds up to seven digits are accepted.
  The period is `[validFrom, validUntil)`: the start applies, the end does not.
  Both timestamps are required and the start must precede the end. Past and
  future periods can be saved. Profile reads include only Constraints active
  at the server's current UTC time; stored Constraints are not deleted at expiry.
- Every supplied stable fact replaces its saved value. Constraints append.
  Omitted facts and existing Constraints remain unchanged.
  An initial update may create a profile without body weight. A null field
  behaves like an omitted field; use an empty array to clear a list.
- A batch has 1–100 changes for the authenticated athlete. All changes validate
  before writing. Changes apply in list order in one transaction. Unsupported
  versions and invalid batches leave all facts and freshness unchanged.
- `lastSyncedAt` is the server's UTC acceptance time for the saved batch.
  An empty store returns a null `profile` and `lastSyncedAt`. Profile reads
  carry active Constraints and keep the stored freshness even as time changes.
  An update response also includes `savedConstraints`, including future ones,
  so the agent can confirm what was stored immediately.
- MCP success responses have `structuredContent` and an empty `content` array.
  Update returns the saved facts. Validation errors report no data freshness;
  protocol/binding errors use the SDK's error format.

The update tool sends its batch over HTTP to `/ingest`, forwarding the current
bearer token. The target URL comes from configuration, never the incoming Host
header; redirects are disabled. A boundary test denies ingest authorization
and verifies that the tool cannot persist a value through another route.

## Ingest version 2 and manual Activity sync

Version 2 adds an inclusive `activityRange` and an `activities` array. The array
may be empty when Garmin reports no Activities. `athleteProfiles` is optional;
when present, profile changes and Activities commit in one transaction. Version 1
retains its existing contract.

```json
{"version":2,"activityRange":{"fromDate":"2026-09-21","toDate":"2026-09-22"},"activities":[{"activityId":24444892080,"activityName":"Sunday strength","startTimeUtc":"2026-09-21T14:34:01Z","typeKey":"strength_training","durationSeconds":2820.37890625,"distanceMeters":0,"totalSets":14,"activeSets":14,"totalReps":82}]}
```

Activity IDs must be positive and distinct within a batch; timestamps must be
UTC with `Z`. Duration and distance are in seconds and metres, and strength
totals are nonnegative. Invalid input leaves all collections unchanged. The
optional `activityName` carries Garmin's Activity name (up to 500 characters);
omitting it from a later batch preserves the stored name. `typeKey` identifies
the Activity type, such as running, indoor cycling, or strength training. The
store identifies a version 2 batch by its validated content: reposting the same
batch preserves records and sync times. The `get_sync_status` read-only MCP tool
returns a count and `lastSyncedAt` for each collection, plus the most recent
Activity summary, including its name and type. A null time means no
successful sync; a zero count with a time means a successful empty ActivityRange.

An Activity may carry its ExerciseSets:

```json
{"activityId":24444892080,"startTimeUtc":"2026-09-21T14:34:01Z","typeKey":"strength_training","durationSeconds":2820.4,"exerciseSets":[{"setType":"ACTIVE","repetitions":8,"weightKg":75.0,"bodyweight":false,"exercise":{"category":"BENCH_PRESS","name":null},"candidateCount":3,"topProbability":99.609375,"startTimeUtc":"2026-09-21T14:41:27.0Z","durationSeconds":62.711,"wktStepIndex":4}]}
```

ExerciseSet contract rules:

- The canonical weight unit is kilograms: `weightKg` is nonnegative, below
  1000000, with at most four decimal places. The sync converts Garmin's grams before posting, so
  grams never reach the store, and the export's FIT path converts to the same
  unit at the same boundary.
- A bodyweight working set states `bodyweight: true` and omits `weightKg`.
  `weightKg: 0` is a set performed with no external load, and neither field at
  all means no load was recorded. The three are stored distinctly and never
  collapsed.
- `exercise` is the movement's `category` (required, 1–100 characters) with an
  optional `name`; a category with a null name, such as the flat bench press,
  is a complete Exercise. `candidateCount` (0–999) and `topProbability` (0–100)
  describe Garmin's classification of the movement.
- `setType` is Garmin's value, `ACTIVE` or `REST`, 1–50 nonblank characters.
  An `ACTIVE` set with zero repetitions is a CancelledExerciseSet: stored,
  identifiable in responses, and excluded from volume and per-set averages.
- `repetitions` (0–99999), `startTimeUtc`, `durationSeconds` and `wktStepIndex`
  (0–99999) are optional. At most 1000 ExerciseSets per Activity; list entries
  cannot be null. An invalid ExerciseSet rejects the whole batch.
- Omitting `exerciseSets` from a later batch preserves the stored sets, the way
  an omitted `activityName` preserves the name; an empty array clears them.
- `get_sync_status` reports an `ExerciseSet` collection and, when the most
  recent Activity has stored sets, `latestActivityStrengthDetail`: the rows in
  recorded order plus `workingExerciseSets`, `cancelledExerciseSets`,
  `volumeKg`, and `averageRepetitions`, the figures excluding
  CancelledExerciseSets and counting no stated kilograms for bodyweight sets.

A Cardio Activity may carry its time in each HeartRateZone, and the batch then
carries the SportProfiles those zones are measured against:

```json
{"version":2,"activityRange":{"fromDate":"2026-09-17","toDate":"2026-09-17"},"sportProfiles":[{"name":"DEFAULT","trainingMethod":"HR_RESERVE","restingHeartRateBpm":51,"maxHeartRateBpm":180,"lactateThresholdHeartRateBpm":null,"heartRateZones":[{"zoneNumber":1,"lowBoundaryBpm":116},{"zoneNumber":2,"lowBoundaryBpm":128}]}],"activities":[{"activityId":24399329227,"startTimeUtc":"2026-09-17T17:19:25Z","typeKey":"running","durationSeconds":2300.68994140625,"timeInHeartRateZones":[{"zoneNumber":1,"seconds":591.39},{"zoneNumber":2,"seconds":378.007}]}]}
```

Time in zones contract rules:

- `timeInHeartRateZones` holds at most 20 entries, each a distinct `zoneNumber`
  (1–20) with finite `seconds` from 0 to 1000000000. An absent zone is unknown,
  never zero seconds, and the seconds need not sum to the Activity's duration.
- An Activity carrying time in zones requires `sportProfiles` in the same batch.
  It holds at most 20 profiles with distinct `name`s (1–50 characters), and
  must include `DEFAULT`. Each has 1–20 `heartRateZones` with distinct
  `zoneNumber`s whose `lowBoundaryBpm` (1–300) rises with the zone number.
  `trainingMethod` and the resting, maximum and lactate-threshold heart rates
  (1–300 bpm) are optional.
- An Activity is measured against the override for its sport (`RUNNING`,
  `CYCLING` or `SWIMMING`, matched from its `typeKey`) when the batch carries
  one, and against `DEFAULT` otherwise.
- Boundaries that differ from a profile's latest stored ones are stored
  alongside them as a new revision, observed at the batch's acceptance time.
  An Activity keeps the revision it was first stored with time in zones
  against: re-sending it after the boundaries change updates its seconds but
  not its boundaries. Only a changed `typeKey` moves it to the current revision
  of its new sport's SportProfile.
- Omitting `timeInHeartRateZones` preserves the stored seconds; an empty array
  clears them but keeps the revision for when seconds are sent again.
- Time in zones is accepted for any `typeKey`; the sync sends it for Cardio
  Activities only.
- `get_sync_status` reports a `SportProfile` collection, its time being the last
  batch that delivered boundaries, and, when the most recent Activity has time in
  zones, `latestActivityCardioDetail`: its `sportProfile` with boundaries,
  `sportProfileObservedAt`, and `timeInHeartRateZones`. The seconds are Garmin's,
  measured against the boundaries observed on that date.

Run the manual sync on the home machine with Python installed and the Garmin
token file already saved at `~/.garminconnect/garmin_tokens.json`. An explicit
`--token-dir` overrides `$GARMINTOKENS` and that default. Set an access token
for the same audience as MCP in `COACH_ACCESS_TOKEN`, then:

```powershell
python -m pip install -r src/sync/requirements.txt
$env:COACH_INGEST_URL = 'https://<app-host>/ingest'
python src/sync/sync_activities.py --from-date 2026-09-21 --to-date 2026-09-22
```

The sync lists only the requested dates, enriches each strength Activity with
one further call for its exercise sets, waits at least three seconds between
Garmin requests, and posts one version 2 batch with weights converted to
kilograms. Cardio Activities, including any unanticipated sport, cost no further
call: their time in zones is read off the Activity list, and the zone
boundaries are fetched at most once per run, only when an Activity carries
time in zones. A 401, 403, or 429
stops it without retrying; network errors and 5xx responses retry up to three
times with backoff. A missing or expired saved Garmin token requires the
interactive login flow from the strength-data spike before this command runs.
No automated test contacts Garmin.

## Watermark runs

A run without dates continues from the Watermark, the sync's local record of
completed days:

```powershell
$env:COACH_INGEST_URL = 'https://<app-host>/ingest'
$env:COACH_SYNC_EARLIEST_DATE = '2026-09-01'
python src/sync/sync_activities.py
```

Each pending day is fetched most recent first and posted as its own single-day
version 2 batch. The Watermark records a day once its batch is accepted, so an
interrupted run keeps everything already fetched and the next run continues
where it stopped. The current day is synced but never recorded as completed, so
an evening run picks up that day's session and the next run syncs the day again.
A run processes at most `--max-days` days (default 30, or `COACH_SYNC_MAX_DAYS`),
so a long gap closes over several runs, most recent days first. A first run
reaches back to `--earliest-date` (or `COACH_SYNC_EARLIEST_DATE`), which a run
without dates requires; days before it are left to the export backfill. The
Watermark lives at `~/.coach-sync/watermark.json` (`--watermark-file`, or
`COACH_SYNC_WATERMARK`) and an unreadable file stops the run rather than
resyncing everything. Re-running over an already-synced period re-posts only the
current day's identical batch, which the store treats as the same delivery.

## Project boundaries

`OakShore.Coach.Domain` owns profile rules and repository/gateway interfaces and uses
only the BCL. `OakShore.Coach.Infrastructure` owns SQL Server mapping, persistence, and
the ingest HTTP gateway. `OakShore.Coach.Api` owns authentication, endpoint/tool mapping,
and composition in `Program.cs`. Tests assert these dependency directions with
NetArchTest. The Python spike is independent of this slice.
