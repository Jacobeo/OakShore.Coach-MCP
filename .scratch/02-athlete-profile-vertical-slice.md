# Spec 02 — AthleteProfile vertical slice

## Problem Statement

The athlete wants to talk to his own training data from ChatGPT on his laptop and
his phone. Getting there requires a publicly reachable, authenticated, stateless
MCP server, and every part of that path carries a risk that is only discoverable
by trying it:

- ChatGPT offers no static-token option, so OAuth 2.1 is mandatory rather than
  deferrable. Microsoft Entra ID cannot serve this role — it supports neither
  Dynamic Client Registration nor client-ID metadata documents — so the identity
  provider is a third party, and that integration is unproven.
- ChatGPT's connector safety scanner is documented to produce false positives
  triggered by tool-description wording, and phrases around personal information
  are among the reported triggers. This connector is, in its entirety, about
  personal health data.
- ChatGPT does not persist MCP session identifiers between turns, so a stateful
  server produces a re-authentication loop rather than an error message.
- Tool responses are subject to an undocumented size budget that truncates
  mid-line, and tool descriptions to a token cap.
- Azure Container Apps must be configured to stay inside its free grant, and
  several defaults — a container registry, a Log Analytics workspace, warm
  replicas — quietly cost money.

Discovering any of these after the Garmin sync, the schema and the analytics were
built would waste weeks of work on foundations that do not hold.

## Solution

Build and deploy the thinnest possible end-to-end system that exercises the
entire risky path and contains no Garmin code at all: a datastore, an
authenticated ingest endpoint, a stateless .NET MCP server behind OAuth, hosted on
Azure Container Apps, exposing exactly two tools — read and update the
AthleteProfile.

That is enough to hold a real conversation from ChatGPT on desktop and phone, and
it is the smallest thing that proves hosting, auth, protocol, deployment and
client reachability simultaneously. It also delivers something genuinely useful:
the AthleteProfile is a prerequisite for every later feature, and conversational
editing of it is what stops it going stale.

## User Stories

1. As the athlete, I want to add my server to ChatGPT as a custom connector, so that I can use it in ordinary conversations.
2. As the athlete, I want to sign in once through a hosted identity provider, so that my health data is not readable by anyone who guesses the URL.
3. As the athlete, I want the connector to keep working across conversations without repeatedly asking me to sign in, so that it is usable rather than irritating.
4. As the athlete, I want to use the connector from my phone after adding it on desktop, so that I can ask questions away from my computer.
5. As the athlete, I want to add the same server to Claude as well, so that I am not locked to one model vendor.
6. As the athlete, I want to ask what my profile contains and get a readable answer, so that I can confirm the system knows what it should.
7. As the athlete, I want to tell the agent my current body weight in conversation and have it persist, so that I never maintain a config file by hand.
8. As the athlete, I want to state my Goal and its target date conversationally, so that later prescriptions have something to aim at.
9. As the athlete, I want to record which equipment I have access to, so that prescriptions only contain exercises I can actually perform.
10. As the athlete, I want to record how many sessions per week I intend to train and for how long, so that prescriptions match my real availability.
11. As the athlete, I want to record a long-standing limitation such as weak ankles, so that it shapes every future prescription.
12. As the athlete, I want to record a temporary Constraint such as an injury with an expiry, so that it stops applying automatically once it is no longer true.
13. As the athlete, I want an expired Constraint to be excluded from the profile the agent sees, so that a two-week-old knock does not suppress my training forever.
14. As the athlete, I want reading my profile never to prompt me for approval, so that ordinary questions are not interrupted.
15. As the athlete, I want changes to my profile to be confirmed back to me in words, so that I can catch a misunderstanding immediately.
16. As the athlete, I want every response to tell me how fresh its data is, so that I know when I am reasoning over a gap.
17. As the athlete, I want the server's standing cost near zero and preferably below $10/month, so that a personal project does not become an expensive subscription.
18. As the athlete, I want to accept a few seconds of delay on my first question of the day, so that the server can scale to zero and stay free.
19. As a developer, I want only the app's managed identity to have application database permissions, so that public SQL network access does not grant data access.
20. As a developer, I want a health endpoint separate from the MCP endpoint, so that platform probes do not receive JSON-RPC.
21. As a developer, I want the schema to carry an athlete identifier from the first migration, so that a second athlete never requires a rewrite.
22. As a developer, I want data to enter the datastore only through the ingest endpoint, so that validation and authorisation live in exactly one place.
23. As a developer, I want to seed a test database by posting the same payloads the sync will post, so that tests exercise the real contract.
24. As a developer, I want the deployment to be reproducible from the repository, so that redeploying is not an act of memory.

## Implementation Decisions

**Server.** ASP.NET Core using `ModelContextProtocol.AspNetCore` at 2.2.0 or
later, which is stable — Microsoft's own tutorials still instruct installing it
with a prerelease flag, and that guidance is out of date. Hosting is
`AddMcpServer().WithHttpTransport()` with `MapMcp` on a dedicated path.

**Stateless, and dual-era.** Stateless mode, which is the SDK default, because
ChatGPT does not persist session identifiers and a stateful server produces a
re-authentication loop. The server should nonetheless remain compatible with
clients on the older protocol revision that still perform an initialise
handshake, because Anthropic's hosted connector documentation references that
revision and the current behaviour of the hosted client is undocumented.

**Health probe.** A separate lightweight endpoint. The MCP endpoint returns
JSON-RPC and is unsuitable as a probe target.

**CORS.** Enabled explicitly. It is not on by default on Container Apps and its
absence fails in a way that looks like an auth problem.

**Identity.** A hosted identity provider that supports client-ID metadata
documents — the shortlist is WorkOS, Stytch and Scalekit. Entra ID is rejected
and the rejection is deliberate: no DCR, no CIMD, and an application-identifier
requirement that would force a custom domain. Both of ChatGPT's documented
redirect URIs are allowlisted. Note that ChatGPT registers its client once per
connection and cannot re-register, so deleting the client at the identity
provider permanently breaks the connector.

**Authorisation on the server.** Protected resource metadata is published at the
well-known path, with bearer-token validation whose expected audience is the
server's own URL. Failed authorisation returns a challenge naming the metadata
location.

**Tool surface.** Exactly two tools in this spec: read the AthleteProfile, and
update it. Read tools are annotated as read-only so that ChatGPT does not prompt
for approval on every call. The eventual surface is capped at roughly eight
tools, because larger surfaces are documented to degrade tool selection badly.

**Tool responses.** Structured content only. The duplicated serialised-JSON text
block that some servers also emit must be omitted, because responses are subject
to an undocumented budget that truncates mid-line. Every response carries the
timestamp of the data it is derived from.

**Tool descriptions.** Short, concrete, and explicitly non-destructive in
wording. This is a deliberate hedge against the connector safety scanner, whose
documented false-positive triggers overlap heavily with the vocabulary of a
personal health application.

**Datastore.** Azure SQL Database on the lifetime free offer, which provides
sufficient compute and thirty-two gigabytes of data permanently at no cost. To
avoid Container Apps custom-VNet standing charges, the database uses its public
endpoint with Microsoft Entra-only authentication. Azure SQL's Azure-services
firewall rule permits network attempts from other Azure subscriptions; only the
app's managed identity receives database permissions. This is the explicit
cost/isolation trade-off recorded in [the hosting cost decision](../docs/hosting-cost-decision.md).

**Ingest.** A single authenticated endpoint accepting batches. It is the only
write path into the datastore, per ADR 0003's seam and ADR 0002's system-of-record
decision. Its payload contract is versioned from the first commit, because two
runtimes depend on it.

**Schema.** Every table carries an athlete identifier from the first migration.
AthleteProfile holds stable facts; Constraint is a separate concept with a
validity period, and expired Constraints are excluded from reads rather than
deleted, so that history is preserved.

**Hosting.** Azure Container Apps on the consumption plan, scaling to zero. The
container image lives in GitHub Container Registry with a token held as a
Container Apps secret, because Azure Container Registry has no free tier. The
environment's log destination is set to none. Secrets are Container Apps secrets;
Key Vault has no free allowance and is not used. Accepting cold starts is a
deliberate trade: warm replicas cannot fit inside the free grant.

## Testing Decisions

A good test here asserts on behaviour observable at the boundary a client sees. It
calls tools and inspects responses. It does not reach into repositories, assert
on generated SQL, or verify that a particular method was called. If a test would
still pass after the feature was deleted, or fail after a harmless refactor, it is
testing the wrong thing.

**The .NET seam — the MCP tool boundary.** Tests run an in-process ASP.NET test
host against a real SQL Server in a container, not an in-memory substitute, since
much of what can break here is schema and query behaviour. Data is seeded by
POSTing batches through the ingest endpoint, exactly as the sync will. Assertions
are made on MCP tool responses. Everything between those two points is real.

**Modules covered by that seam.** Tool registration and discovery, tool argument
binding, the profile read and update behaviour, Constraint expiry, athlete
scoping, response shape including the freshness timestamp, and the ingest
contract's validation and rejection behaviour.

**Authorisation** is tested at the same seam by asserting that an unauthenticated
request is challenged and that the challenge names the metadata location. The
token issuance flow itself belongs to the identity provider and is not re-tested
here.

**Prior art.** None; this is the first code in the repository. This spec
establishes the pattern, and specs 03 to 05 extend it rather than introducing new
seams.

**Not covered by automated tests**, and verified by hand once: that ChatGPT can
add the connector, complete the OAuth flow, list the tools, call them from
desktop and from the phone, and that Claude can do the same. These depend on
third-party clients and are confirmed by use.

## Out of Scope

Any Garmin code, credentials or data. The Program, Phases, prescription and
Adherence. Activities, ExerciseSets, sleep and load metrics. The scheduled sync.
Push notifications. Any write to Garmin. Support for a second athlete beyond the
schema carrying an identifier.

## Further Notes

The ChatGPT connector path has already been confirmed to work from the athlete's
account and region, which removes the largest single risk this spec was written
to attack. What remains unproven is the OAuth integration and the safety
scanner's reaction to a health-data connector.

If the safety scanner rejects the connector, the documented workaround is
rewriting tool descriptions to stress that operations are read-only and
non-destructive. That is a wording problem, not an architecture problem, but it
is worth discovering here rather than in spec 04 when there are eight tools to
reword.
