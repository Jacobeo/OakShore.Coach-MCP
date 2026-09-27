# 05: Record temporary Constraints and exclude expired ones

**What to build:** The athlete can add a temporary Constraint with a stated
validity period through the existing update tool. Profile reads include active
Constraints and automatically stop returning them when they expire, while
preserving the historical records.

**Blocked by:** 01: Read and update body weight through MCP.

**Status:** done

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [x] The update tool accepts a Constraint and its validity period, persists it
  through authenticated ingest, and returns the saved information for the agent
  to confirm. Constraint storage and queries carry UserId.
- [x] The contract makes period boundaries and time interpretation explicit.
  Invalid periods are rejected without saving the rejected Constraint.
- [x] Reads return Constraints active at the time of the read. Future Constraints
  do not apply early, and expired Constraints stop appearing without a new ingest
  batch, manual cleanup, or changes to stable AthleteProfile facts.
- [x] Expiration filters reads rather than deleting stored Constraints. Demonstrate
  active, future, expired, and boundary cases through the existing real SQL Server
  MCP test seam, including retained-history behaviour using controlled time.
- [x] Adding a Constraint preserves body weight and other existing facts and
  Constraints. A caller cannot read or add Constraints for another UserId.
- [x] Both tools continue to use structuredContent only with data freshness;
  freshness describes stored data rather than becoming the current time merely
  because a Constraint stopped applying.
- [x] Tests seed through authenticated ingest and exercise the actual MCP tools,
  without repository mocks or a new test seam. No additional tool, Macrocycle,
  prescription, or Garmin behaviour is introduced.
- [x] Domain vocabulary and dependency rules remain enforced; all added files
  appear in the solution. The behaviour is independently demonstrable after
  ticket 01 and does not depend on ticket 04's additional stable facts.
