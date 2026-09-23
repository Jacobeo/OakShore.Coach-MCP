# 03: Connect ChatGPT and Claude through hosted OAuth

**What to build:** The athlete can connect the deployed server to ChatGPT and
Claude, authenticate through a hosted provider, and hold conversations that read
and change persisted body weight. ChatGPT also works from the athlete's phone
after desktop setup, without a repeated sign-in loop.

**Blocked by:** 02: Provision infrastructure and deploy the body-weight slice.

**Status:** ready-for-agent

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [ ] Select and configure a hosted identity provider from the spec's shortlist
  after verifying current client-ID metadata support and suitability for the
  project's cost requirement. Record the selection and reproducible setup steps;
  Entra ID is not substituted for the provider rejected by the spec.
- [ ] Configure the OAuth flow, both current documented ChatGPT redirect URIs,
  and the settings needed by Claude. Record any account-owner setup steps and
  complete them with the athlete as needed.
- [ ] The deployed protected-resource metadata identifies the configured issuer;
  bearer-token validation checks that issuer and the server URL as audience.
  Challenges point clients to the metadata, and invalid or wrong-audience tokens
  cannot access AthleteProfile data.
- [ ] ChatGPT can add the connector, complete OAuth, discover exactly the two
  tools, and read persisted body weight from Azure on desktop and phone. Record
  dated evidence: this demonstrates build step 2's done-when condition.
- [ ] A desktop conversation updates body weight and confirms the saved value
  in words; a subsequent read, including from the phone, returns that value and
  its freshness. Reading the profile does not require per-call approval.
- [ ] Verify continued use across turns and new conversations without a
  repeated re-authentication loop. Verify cold-start behaviour with the connector.
- [ ] Claude can connect, list the tools, and read and update the same
  AthleteProfile. Confirm compatibility with its actual handshake behaviour.
- [ ] Verify connector acceptance with the actual descriptions. If wording must
  change, keep it truthful about read and update effects, preserve structured-only
  responses, and rerun affected boundary checks and client acceptance checks.
- [ ] Document how to preserve the identity-provider client registration during
  redeployment and maintenance; include the source spec's warning that deleting
  ChatGPT's registered client breaks that connection.
- [ ] Keep automated authorization tests at the existing boundary seam. Record
  real-client acceptance separately; do not replace it with simulated token
  issuance or mocks. All added files appear in the solution.

