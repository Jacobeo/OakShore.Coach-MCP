# 02: Provision infrastructure and deploy the body-weight slice

**What to build:** Provision the infrastructure from the repository and deploy
the working body-weight slice to Azure. An authenticated client can save and read
body weight from the deployed service, including after a redeployment. Resource
creation, schema deployment, image publication, and configuration are repeatable.

**Blocked by:** 01: Read and update body weight through MCP.

**Status:** ready-for-agent

**Source:** [AthleteProfile vertical slice](../../02-athlete-profile-vertical-slice.md).

- [ ] Repository-managed infrastructure definitions provision the resource group,
  Container Apps environment and application, Azure SQL server and database,
  required network connectivity and private DNS, identities, and access permissions.
  Document required account access and deployment inputs without storing secrets.
- [ ] The database has public network access disabled. Demonstrate that the
  application reaches it through private connectivity and that a public client
  cannot connect directly to it.
- [ ] Build and publish the application image to GitHub Container Registry and
  configure Container Apps to pull it. Registry credentials and application
  secrets are supplied through Container Apps secrets.
- [ ] Configure consumption hosting, zero minimum replicas, log destination none,
  and the Azure SQL free offer. Do not provision Azure Container Registry, a Log
  Analytics workspace, Key Vault, or warm replicas as incidental defaults.
- [ ] Validate current offer eligibility and the cost of every provisioned
  component, including networking, against the spec's zero-cost requirement.
  If private networking and the free requirement cannot both be met, document
  the conflict for a decision before provisioning chargeable alternatives or
  relaxing database isolation.
- [ ] Deploy schema changes through a repeatable process that works with the
  private database and preserves stored AthleteProfile data on redeployment.
- [ ] Configure external HTTPS ingress, explicit CORS for the supported client
  flow, the server's audience URL, and separate health probes. Protected routes
  remain authenticated throughout deployment.
- [ ] Use a valid token from a configured issuer to demonstrate ingest, MCP read,
  and MCP update against Azure. Confirm that unauthorized requests fail. Hosted
  connector registration and interactive OAuth acceptance belong to ticket 03.
- [ ] Record evidence that data survives redeployment and that the service can
  scale to zero and serve a subsequent request after a cold start.
- [ ] Document and exercise resource provisioning and redeployment from the
  repository, including the authenticated smoke check. Add all infrastructure,
  deployment, and documentation files to the solution.

