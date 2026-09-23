# Ticket 02: private SQL access and the zero-cost requirement

**Decision pending. Do not provision Azure resources for ticket 02 until the
hosting and database-networking choice is made.** This assessment was checked
against Azure documentation on 2026-09-23. Subscription-specific offers and
prices must also be checked in the target subscription before deployment.

Ticket 02 requires Azure SQL public network access to be **Disabled** and the
entire service to cost nothing. Those requirements cannot both be met with its
specified Azure Container Apps and Azure SQL topology:

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

The current Azure CLI account context exists, but a resource inventory request
returned `Status_InteractionRequired`; free-offer eligibility, regional
availability, existing resources, and effective subscription prices have not
yet been verified. No resource was provisioned during this assessment.

Choose one path before writing or running the provisioning deployment:

1. Keep strict zero cost. Change the architecture or defer deployment; preserve
   SQL isolation while the replacement is decided.
2. Allow the standing network charges with an explicit budget. Keep SQL public
   access disabled and use a SQL private endpoint, private DNS, and a VNet
   integrated Container Apps environment.
3. Change the isolation requirement. A SQL service endpoint could restrict the
   reachable public SQL endpoint to the app subnet, but this requires a spec
   change and still leaves Container Apps VNet integration charges to assess.

None of these is an implicit exception to the ticket. Record the selected path
and a subscription-specific cost estimate before provisioning.
