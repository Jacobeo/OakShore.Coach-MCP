# coach-mcp specs

Build order and specs, derived from the design conversation. Vocabulary follows
[CONTEXT.md](../CONTEXT.md); decisions already fixed are in [docs/adr](../docs/adr).

## What this is

A personal training coach whose system of record is its own datastore. A Python
sync runs nightly at home, pulls Activities, ExerciseSets, sleep and load metrics
from Garmin Connect, and POSTs them to an authenticated ingest endpoint on a
stateless .NET MCP server hosted on Azure Container Apps. The server exposes
around eight curated tools to ChatGPT and Claude behind OAuth. The headline tool
takes recent history, the athlete's Macrocycle and current form, and prescribes the
next block of training.

## Build order

| # | Spec | Why here |
| --- | --- | --- |
| 01 | [Strength data spike](01-strength-data-spike.md) | Validates the assumption the whole product rests on, for an hour's work |
| 02 | [AthleteProfile vertical slice](02-athlete-profile-vertical-slice.md) | Proves hosting, auth and client reachability with no Garmin involved |
| 03 | [Garmin sync](03-garmin-sync.md) | The fragile part, attempted only once the rest is known to work |
| 04 | [Macrocycle and prescription](04-macrocycle-and-prescription.md) | The actual use case |
| 05 | [Garmin write-back](05-garmin-writeback.md) | Closes the loop onto the watch |

Specs 01 and 02 exist to attack the two largest unknowns first: whether the
strength data is real, and whether the ChatGPT plus OAuth plus Azure path holds.
Either could reshape everything downstream, and both are cheap to answer.

## Test seams

Two seams, one per runtime. Prefer these over any new ones.

**The .NET seam — the MCP tool boundary.** Tests run an in-process ASP.NET test
host against a real SQL Server container. Data is seeded by POSTing fixture
batches through the ingest endpoint, exactly as the sync would. Assertions are
made on MCP tool responses. Nothing between those two points is mocked, so
schema, repositories, analytics and tool serialisation are all covered by one
seam.

**The Python seam — the Garmin client boundary.** Real Garmin responses captured
by spec 01 are replayed as fixtures. The sync under test is asserted on the
batches it POSTs to ingest.

The two seams meet at the ingest contract, which is the only place the runtimes
touch. Spec 01's raw dumps are the fixture corpus for both.
