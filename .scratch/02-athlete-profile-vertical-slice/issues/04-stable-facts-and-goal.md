# 04: Maintain stable AthleteProfile facts and Goal

**What to build:** The athlete can maintain available equipment, intended weekly
training frequency and duration, lasting limitations, and a Goal with its target
date through the existing AthleteProfile tools. Each change can be confirmed
from the saved response and retrieved in a later conversation.

**Blocked by:** 01: Read and update body weight through MCP.

**Status:** ready-for-agent

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [ ] The two existing tools accept and return equipment, training frequency and
  duration, lasting limitations, and Goal with target date, alongside body weight.
  No Program, Phase, prescription, Garmin integration, or additional tool is added.
- [ ] Updates persist through authenticated ingest and can create an
  AthleteProfile when one is absent. Updating one supplied fact preserves
  unrelated facts; omitted fields do not silently erase stored values.
- [ ] The contract states units and validates supported values and Goal dates.
  Invalid changes produce clear structured feedback without persisting rejected
  values or disturbing unrelated facts.
- [ ] The saved response contains enough information for the agent to confirm
  the change accurately. Reads and updates carry freshness and structuredContent
  only; read-only annotation and stateless transport remain intact.
- [ ] Lasting limitations remain stable AthleteProfile facts, distinct from the
  expiring Constraints introduced by ticket 05.
- [ ] Real SQL Server boundary tests seed through ingest and verify MCP argument
  binding, creation, each supported fact, partial-update preservation, validation,
  persistence, freshness, and isolation between UserIds.
- [ ] Domain vocabulary and dependency rules remain enforced; all added files
  appear in the solution. This ticket is independently demonstrable through the
  local MCP boundary and does not wait for infrastructure or client setup.

