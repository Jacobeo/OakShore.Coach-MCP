# Ticket 02: low-cost hosting and SQL network access

**Decision, 2026-09-23:** Keep Azure Container Apps and target approximately
$0 standing cost, preferably below $10/month. Use Azure SQL's public endpoint
instead of a private endpoint and customer VNet. The athlete explicitly chose
this isolation trade-off after reviewing the private option's cost. Verify the
SQL free offer in the target subscription before provisioning; never substitute
a paid database automatically.

The SQL server uses Microsoft Entra-only authentication. The app connects with
a dedicated managed identity granted access to its database. SQL's
`AllowAzureServices` firewall rule allows connection attempts from **all Azure
subscriptions**, not just this app or subscription. Azure SQL still rejects
callers without database permission. This is a material exposure of the public
endpoint, and the documented low-cost design accepts it for this personal
project. See [Microsoft's firewall description](https://learn.microsoft.com/en-us/azure/azure-sql/database/firewall-configure?view=azuresql).

No customer VNet, private endpoint, private DNS zone, Azure load balancer,
Azure Container Registry, Log Analytics workspace, or warm replica is needed
for this topology. Container Apps and SQL usage remain subject to their free
grants, so the target is not a guaranteed cost cap. The cost assessment below
explains the private alternative that was rejected.

| Provisioned component | Expected standing charge | Usage condition |
| --- | ---: | --- |
| Resource group and managed identity | $0/month | No separate meter for these resources. |
| Container Apps consumption environment and app | $0/month | Zero minimum replicas, no log destination or customer VNet; compute and requests must remain within the [monthly free grant](https://learn.microsoft.com/en-us/azure/container-apps/billing). |
| Azure SQL `coach` database | $0/month | Subscription's [free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql) is active; `AutoPause` stops database compute when its monthly allowance is exhausted. |
| GitHub Container Registry | $0/month currently | [GitHub's current package billing policy](https://docs.github.com/en/billing/concepts/product-billing/github-packages) does not charge for container-image storage or bandwidth. |
| WorkOS AuthKit Staging | $0/month | [Staging environment](https://workos.com/docs/authkit/environments) is used for setup and testing. |
| Cross-region and internet transfer | Variable | North Europe app and West Europe SQL can incur transfer charges; inspect actual usage. |

This is an expected near-zero standing charge, not a $10 spending cap.

The original ticket required Azure SQL public network access to be **Disabled**
and the entire service to cost nothing. Those requirements could not both be
met with its specified Azure Container Apps and Azure SQL topology:

- With SQL public access disabled, [Azure SQL private connectivity](https://learn.microsoft.com/en-us/azure/azure-sql/database/private-endpoint-overview?view=azuresql)
  uses a private endpoint. [Private Link charges](https://azure.microsoft.com/en-us/pricing/details/private-link/)
  apply per endpoint-hour and for data processed.
- The Container Apps environment must join a customer VNet to reach the SQL
  private endpoint. [Custom VNet integration](https://learn.microsoft.com/en-us/azure/container-apps/custom-virtual-networks)
  adds billed managed network resources, including standard public IPs and a
  load balancer for an externally reachable environment.
- The [private DNS zone](https://azure.microsoft.com/en-us/pricing/details/dns/)
  required to resolve the SQL server to the endpoint's private IP has a zone
  charge and query charges.
- [SQL virtual-network service endpoints](https://learn.microsoft.com/en-us/azure/azure-sql/database/vnet-service-endpoint-rule-overview?view=azuresql)
  are free, but require SQL public network access set to **Selected networks**.
  They therefore do not satisfy the ticket's **Disabled** requirement.

## Monthly estimate for the specified architecture

The following is a **retail estimate in USD** for an external Container Apps
workload-profile environment in **West Europe**, with one SQL private endpoint,
one private DNS zone, one app, and a 730-hour month. Meter rates were checked
against the [Azure Retail Prices API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
on 2026-09-23. [Container Apps VNet documentation](https://learn.microsoft.com/en-us/azure/container-apps/custom-virtual-networks)
says an external environment in a custom VNet is billed for two standard static
public IPs and one standard load balancer. The private endpoint here is for
**SQL outbound access**, not an additional private endpoint for app ingress.

| Standing component | Rate | 730-hour month |
| --- | ---: | ---: |
| SQL private endpoint | $0.010/hour | $7.30 |
| Container Apps managed standard load balancer | $0.025/hour | $18.25 |
| Two Container Apps managed standard public IPs | 2 × $0.005/hour | $7.30 |
| SQL private DNS zone (first 25 zones tier) | $0.50/month | $0.50 |
| **Estimated standing total** | | **$33.35/month** |

That is about **$400/year**, even when the app has zero replicas. A 31-day
month is about $34.00. The estimate excludes taxes and any negotiated
subscription discount. Validate the actual meter rates and managed resources
in Azure Cost Analysis after deployment; pricing and billing mappings can
change.

This topology does not require a NAT Gateway, a [Private DNS Resolver](https://learn.microsoft.com/en-us/azure/private-link/private-endpoint-dns-integration),
or a private endpoint targeting Container Apps itself. The latter would add
[a separate Container Apps management charge](https://learn.microsoft.com/en-us/azure/container-apps/private-endpoints-with-dns).

Usage adds roughly $0.01 per GB in each direction through the SQL private
endpoint, $0.005 per GB processed by the load balancer, and $0.40 per million
private DNS queries at the first tier. For a single athlete's body-weight
profile these should be small compared with the standing total, but are not a
hard cap. Container Apps compute and requests can remain within its
[monthly free grant](https://learn.microsoft.com/en-us/azure/container-apps/billing)
of 180,000 vCPU-seconds, 360,000 GiB-seconds, and two million requests.
The [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql)
provides 100,000 vCore-seconds and 32 GB each of data and backup storage per
month when eligible; select **auto-pause until next month** at its limit to
avoid SQL overage. The [GitHub Container Registry currently does not bill for
container-image storage or bandwidth](https://docs.github.com/en/billing/concepts/product-billing/github-packages),
although GitHub says this policy can change with notice. A hosted identity
provider's plan, Azure outbound internet traffic, and usage beyond the free
grants are outside the $33.35 standing estimate.

The remaining components do not remove these standing network charges:

| Component | Cost condition to validate before deployment |
| --- | --- |
| Resource group, VNet, subnets, managed identity | No base charge for these resources; inspect any attached services. |
| [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql) | Eligible subscription and available free database slot; select auto-pause at the monthly limit to prevent SQL overage. |
| [Container Apps consumption](https://learn.microsoft.com/en-us/azure/container-apps/structure) | Zero minimum replicas; usage must remain inside the subscription's free grant. |
| [GitHub Container Registry](https://docs.github.com/en/billing/concepts/product-billing/github-packages) | Container-image storage and bandwidth are currently free; verify that this policy still applies at deployment. |
| External identity provider | Check the selected provider's current plan and token issuance limits in ticket 03. |

The active subscription was reached after a fresh sign-in. North Europe
rejected new Azure SQL server creation on 2026-09-23, so the app environment
and managed identity were provisioned there while SQL was placed in West
Europe. Azure reported the `coach` database on the free offer with
`AutoPause`, Entra-only server authentication, and no stored Container Apps
log destination. The Container App is deployed with zero minimum replicas and
a private GHCR image credential. Effective charges still need checking in Cost
Analysis after usage begins. Cross-region app-to-SQL traffic can add transfer
charges.
The resource group's Azure Cost Management query returned no rows on deployment
day. On 2026-09-25, its month-to-date ActualCost query reported 0.00 DKK for
each of September 23, 24, and 25. Continue checking as usage posts; these
early rows do not establish a monthly cost cap.

The chosen public-SQL topology avoids these private-network standing charges.
Check the actual subscription's SQL free-offer eligibility, Container Apps
free-grant usage, GHCR terms, and any outbound transfer charges during rollout.
