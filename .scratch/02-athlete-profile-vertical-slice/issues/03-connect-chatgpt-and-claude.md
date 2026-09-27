# 03: Connect ChatGPT through hosted OAuth

**What to build:** The athlete can connect the deployed server to ChatGPT,
authenticate through a hosted provider, and hold conversations that read and
change persisted body weight. The connection also works from the athlete's
phone after desktop setup, without a repeated sign-in loop.

**Blocked by:** 02: Provision infrastructure and deploy the body-weight slice.

**Status:** ready-for-agent

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

**Scope:** ChatGPT only. Claude acceptance in the source spec is deferred.

- [x] Select and configure a hosted identity provider from the spec's shortlist
  after verifying current client-ID metadata support and suitability for the
  project's cost requirement. Record the selection and reproducible setup steps;
  Entra ID is not substituted for the provider rejected by the spec.
- [x] Connect ChatGPT through the provider's supported OAuth client
  registration. Record the account-owner setup and maintenance steps and any
  callback details the client exposes.
- [x] The deployed protected-resource metadata identifies the configured issuer;
  bearer-token validation checks that issuer and the server URL as audience.
  Challenges point clients to the metadata, and invalid or wrong-audience tokens
  cannot access AthleteProfile data.
- [x] ChatGPT can add the connector, complete OAuth, discover exactly the two
  tools, and read persisted body weight from Azure on desktop and phone. Record
  dated evidence: this demonstrates build step 2's done-when condition.
- [x] A desktop conversation updates body weight and confirms the saved value
  in words; a subsequent read, including from the phone, returns that value and
  its freshness. Reading the profile does not require per-call approval.
- [x] Verify continued use across turns and new conversations without a
  repeated re-authentication loop. Verify cold-start behaviour with the connector.
- [x] Verify connector acceptance with the actual descriptions. If wording must
  change, keep it truthful about read and update effects, preserve structured-only
  responses, and rerun affected boundary checks and client acceptance checks.
- [x] Document how to preserve the identity-provider client registration during
  redeployment and maintenance; include the source spec's warning that deleting
  ChatGPT's registered client breaks that connection.
- [x] Keep automated authorization tests at the existing boundary seam. Record
  real-client acceptance separately; do not replace it with simulated token
  issuance or mocks. All added files appear in the solution.

## Comments

- 2026-09-23: WorkOS AuthKit was selected from the spec's shortlist after
  checking current MCP support and pricing. It supports client ID metadata
  documents and dynamic client registration. Its staging environment is free;
  production requires billing details and AuthKit stays free within its
  published user allowance. The athlete created a Staging account. Its public
  issuer and signing-key discovery were verified, and the Coach `/mcp` URL was
  registered as the default resource indicator. Real client sign-in and
  Production setup remain pending.
- 2026-09-25: ChatGPT-only implementation pass. WorkOS Staging's live OAuth
  discovery advertises the expected issuer, `S256`, client ID metadata
  documents, dynamic registration, and token endpoint authentication method
  `none`. It does not advertise issuer identification. Current OpenAI guidance
  therefore calls for the callback-specific redirect URI shown on ChatGPT's
  connection management page, rather than assuming the stable callback. The
  ChatGPT setup and desktop/phone acceptance steps are in README.md. No browser
  session was available to create the connection or observe client acceptance.
  Initial deployed `/health` and resource metadata requests timed out while the
  revision scaled from zero. A later health request succeeded within a 120-second
  timeout, and resource metadata then returned the deployed `/mcp` audience and
  WorkOS issuer. The desktop and phone acceptance checks remain open.
- 2026-09-27: The source spec's instruction to allowlist both then-documented
  ChatGPT redirect URIs was superseded for this connection by current OpenAI
  guidance: WorkOS discovery does not advertise issuer identification, so
  ChatGPT selects a callback-specific URI. The athlete added Coach with the
  server URL and completed WorkOS sign-in without manually entering a callback.
  ChatGPT's management page reports OAuth but does not show the registration
  method or actual callback URI. Their exact values remain unobserved; neither
  is inferred from the successful sign-in.
- 2026-09-27: ChatGPT initially rejected app creation with a generic error. A
  retry while the app was warm reached the Coach connection screen. After WorkOS
  sign-in, ChatGPT allowed `@Coach` but reported no profile tool; the app details
  listed no tools. Reconnecting the linked account made the read and write tools
  appear. In a desktop conversation, `get_athlete_profile` returned the saved
  body weight and `lastSyncedAt` without a read approval prompt. A subsequent
  `update_athlete_profile` saved the athlete's chosen value, ChatGPT confirmed
  it in words, and a later read returned the same value with freshness. A new
  desktop conversation and ChatGPT on the athlete's phone read the updated
  value and freshness without another WorkOS sign-in or per-read approval.
  After Azure reported zero replicas, the first ChatGPT read returned the saved
  data in about 42 seconds without a new sign-in or read approval prompt. This
  is the ChatGPT desktop-and-phone evidence for
  build step 2's done-when condition; Claude is outside this pass.
