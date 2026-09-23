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

The remaining components do not remove these standing network charges:

| Component | Cost condition to validate before deployment |
| --- | --- |
| Resource group, VNet, subnets, managed identity | No base charge for these resources; inspect any attached services. |
| [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql) | Eligible subscription and available free database slot; select auto-pause at the monthly limit to prevent SQL overage. |
| [Container Apps consumption](https://learn.microsoft.com/en-us/azure/container-apps/structure) | Zero minimum replicas; usage must remain inside the subscription's free grant. |
| [GitHub Container Registry](https://docs.github.com/en/billing/concepts/product-billing/github-packages) | Registry storage and transfer depend on account plan, repository visibility, and use. Verify the account's allowance. |
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
