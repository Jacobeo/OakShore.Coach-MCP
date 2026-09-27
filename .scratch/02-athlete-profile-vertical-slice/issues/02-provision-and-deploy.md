# 02: Provision infrastructure and deploy the body-weight slice

**What to build:** Provision the infrastructure from the repository and deploy
the working body-weight slice to Azure. An authenticated client can save and read
body weight from the deployed service, including after a redeployment. Resource
creation, schema deployment, image publication, and configuration are repeatable.

**Blocked by:** 01: Read and update body weight through MCP.

**Status:** needs-info

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [x] Repository-managed infrastructure definitions provision the resource group,
  Container Apps environment and application, Azure SQL server and database,
  identities, firewall rule, and database access permissions.
  Document required account access and deployment inputs without storing secrets.
- [x] The database uses its public endpoint with Microsoft Entra-only
  authentication. The Container App uses its managed identity, and only that
  identity is granted application database permissions. Document and verify the
  Azure-services firewall rule's reachability from other Azure subscriptions.
- [x] Build and publish the application image to GitHub Container Registry and
  configure Container Apps to pull it. Registry credentials and application
  secrets are supplied through Container Apps secrets.
- [x] Configure consumption hosting, zero minimum replicas, log destination none,
  and the Azure SQL free offer. Do not provision Azure Container Registry, a Log
  Analytics workspace, Key Vault, or warm replicas as incidental defaults.
- [x] Validate current offer eligibility and the cost of every provisioned
  component against the preferred $10/month ceiling. Fail provisioning if the
  Azure SQL free offer or auto-pause-on-limit is unavailable; do not substitute
  a paid database. Record the public-SQL isolation trade-off.
- [x] Deploy schema changes through a repeatable process that works with Azure
  SQL and preserves stored AthleteProfile data on redeployment.
- [x] Configure external HTTPS ingress, explicit CORS for the supported client
  flow, the server's audience URL, and separate health probes. Protected routes
  remain authenticated throughout deployment.
- [x] Use a valid token from a configured issuer to demonstrate ingest, MCP read,
  and MCP update against Azure. Confirm that unauthorized requests fail. Hosted
  connector registration and interactive OAuth acceptance belong to ticket 03.
- [x] Record evidence that data survives redeployment and that the service can
  scale to zero and serve a subsequent request after a cold start.
- [x] Document and exercise resource provisioning and redeployment from the
  repository, including the authenticated smoke check. Add all infrastructure,
  deployment, and documentation files to the solution.

## Comments

- 2026-09-23: The athlete chose Container Apps and a roughly $10/month ceiling,
  and explicitly allowed public Azure SQL rather than private endpoint/VNet
  networking. Keep Entra-only database authentication and a dedicated app
  identity. See [the cost decision](../../../docs/hosting-cost-decision.md).
- 2026-09-23: Bicep templates, image publication, deployment, and smoke scripts
  are prepared and statically validated. The hosted token issuer is not chosen,
  and the GHCR token needs setup. Only an empty Azure resource group exists;
  live deployment and authenticated smoke remain pending.
- 2026-09-23: WorkOS Staging issuer configured. Azure foundation deployed with
  the Container Apps environment in North Europe and free-offer SQL in West
  Europe after North Europe rejected new SQL server creation. Verified SQL
  AutoPause, Entra-only authentication, and no Container Apps log destination.
  Image publication, app deployment, and live smoke remain pending.
- 2026-09-23: Granted the Container App managed identity access to the `coach`
  database with an Azure CLI SQL access token. Verified that the temporary
  deployment firewall rule was removed afterward.
- 2026-09-23: Published the image to GHCR and deployed the Container App at
  `https://oakshore-coach-api.yellowpond-6d7fb869.northeurope.azurecontainerapps.io`.
  `/health` returns `ok`; OAuth resource metadata names the WorkOS issuer and
  `/mcp` audience; unauthenticated `/mcp` and `/ingest` both return 401. Azure
  reports zero minimum replicas, consumption workload, no log destination, and
  SQL free limit with AutoPause. Authenticated write/read, redeploy persistence,
  cold start, and actual billing evidence remain pending.
- 2026-09-23: A real WorkOS authorization-code login issued a token with the
  deployed `/mcp` audience. Authenticated ingest wrote an approved 80.0 kg
  sample; MCP updated it to 80.1 kg and read it back. After deploying a new
  GHCR digest, MCP read 80.1 kg with the original sync timestamp. WorkOS's
  device-code approval returned an activation error in Staging, so the browser
  authorization-code flow was used for the test.
- 2026-09-23: Azure reported the active revision `ScaledToZero` with zero
  replicas. The next authenticated smoke check cold-started the app in 36.6
  seconds and read 80.1 kg with the original sync timestamp. Azure Cost
  Management returned no posted billing rows for the new resource group on
  deployment day; inspect actual charges when usage posts.
- 2026-09-25: WorkOS Staging contains one registered user. The athlete confirmed
  that public sign-up was disabled under Authentication → Features → Sign-up.
- 2026-09-25: Azure Cost Management's resource-group ActualCost query showed
  0.00 DKK for each of September 23, 24, and 25. Later usage may still accrue.
- 2026-09-25: A `/health` request after idle timed out at 60 seconds. Azure
  then reported one healthy running replica, and an immediate retry returned
  `{"status":"ok"}`. Cold-start latency can exceed a client's timeout.
