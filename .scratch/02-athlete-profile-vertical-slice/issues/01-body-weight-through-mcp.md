# 01: Read and update body weight through MCP

**What to build:** An athlete can read an empty AthleteProfile, save body weight,
and retrieve the persisted value through exactly two stateless MCP tools. This
is the smallest local path through authentication, ingest, storage, and tool
responses, with no Garmin dependency.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [ ] The read and update tools are discoverable, bind their arguments correctly,
  and support creating and updating body weight. An absent AthleteProfile is
  distinguishable from a stored value; a later read returns the saved value.
- [ ] All writes, including changes initiated by the update tool, pass through
  the authenticated ingest endpoint, as specified by the source. The endpoint
  accepts versioned batches and rejects unsupported versions and invalid input
  without persisting the rejected input.
- [ ] Every table and query carries UserId. Authenticated identity determines
  access; supplying another athlete's identifier cannot expose or modify their
  data. Tests cover both read and write isolation.
- [ ] MCP and ingest require authentication. Bearer-token validation rejects
  invalid tokens and the wrong audience. The expected audience is configurable
  as the server URL; unauthenticated requests receive a challenge naming the
  published protected-resource metadata location. Hosted token issuance is
  configured in ticket 03 rather than reproduced in tests.
- [ ] Responses use structuredContent without a duplicate serialized JSON text
  block. Each response carries data freshness, with explicit behaviour when no
  data exists. Updates return the saved facts so the agent can confirm them.
- [ ] The read tool is annotated read-only. Both tool descriptions are short,
  concrete, and accurate about their effects; the update description does not
  claim that updates are read-only.
- [ ] The HTTP MCP transport works without a retained session identifier and
  supports clients that perform an initialization handshake. A separate
  lightweight health endpoint responds independently of MCP.
- [ ] Boundary tests run an in-process ASP.NET host against a real SQL Server
  container, seed through authenticated ingest, and assert on MCP responses.
  They cover persistence across host recreation, discovery, binding, validation,
  authorization, isolation, and response shape without mocking repositories.
- [ ] Domain, infrastructure, and API responsibilities follow the repository's
  dependency rules. NetArchTest architecture checks accompany the first
  application code, and all added files appear in the solution.

