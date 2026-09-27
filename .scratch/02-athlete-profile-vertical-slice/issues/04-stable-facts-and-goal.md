# 04: Maintain stable AthleteProfile facts and prioritized Goals

**What to build:** The athlete can maintain available equipment, intended weekly
training frequency and duration, lasting limitations, and ordered Goals with a
shared target date through the existing AthleteProfile tools. Each change can be confirmed
from the saved response and retrieved in a later conversation.

**Blocked by:** 01: Read and update body weight through MCP.

**Status:** done

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [x] The two existing tools accept and return equipment, training frequency and
  duration, lasting limitations, and prioritized Goals with one shared target
  date, alongside body weight. No Macrocycle, Mesocycle, prescription, Garmin
  integration, or additional tool is added.
- [x] Updates persist through authenticated ingest and can create an
  AthleteProfile when one is absent. Updating one supplied fact preserves
  unrelated facts; omitted fields do not silently erase stored values.
- [x] The contract states units and validates supported values and the shared date.
  Invalid changes produce clear structured feedback without persisting rejected
  values or disturbing unrelated facts.
- [x] The saved response contains enough information for the agent to confirm
  the change accurately. Reads and updates carry freshness and structuredContent
  only; read-only annotation and stateless transport remain intact.
- [x] Lasting limitations remain stable AthleteProfile facts, distinct from the
  expiring Constraints introduced by ticket 05.
- [x] Real SQL Server boundary tests seed through ingest and verify MCP argument
  binding, creation, each supported fact, partial-update preservation, validation,
  persistence, freshness, and isolation between UserIds.
- [x] Domain vocabulary and dependency rules remain enforced; all added files
  appear in the solution. This ticket is independently demonstrable through the
  local MCP boundary and does not wait for infrastructure or client setup.

## Outcome

Both MCP tools expose the saved stable facts and ordered Goals with one UTC
target date. Version 1 ingest accepts partial changes, validates the full
batch, and saves the result in one SQL Server transaction. The migration keeps
existing body weights and moves an existing single Goal into the prioritized
list. The root README states the field names, units, bounds, and clearing
behavior. Boundary tests confirm creation, every fact, preservation, rejection,
freshness, UserId isolation, and persistence after host recreation. The
build-order step 2 desktop and phone criterion was demonstrated for the
body-weight slice in ticket 03; this ticket's additional facts were verified
locally at the MCP boundary.
