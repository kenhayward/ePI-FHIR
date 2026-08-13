# policies/ - Authorization policies and business rules (config-as-data)

Externalised policy and rules, per D3 ADR-012 and capabilities 17 (RBAC/ABAC) and 21
(config-as-data rules). Kept in the repository so they are versioned, reviewed, and testable.

## Contents
- `authz/` - Open Policy Agent (OPA) **Rego** policies for RBAC/ABAC decisions. Scopes include
  affiliate/organisation, region/market, product & label, lifecycle state, and template.
- `rules/` - business/compliance and lifecycle rules (e.g. DMN decision tables) consumed by
  validation (11), compliance (12), and the lifecycle state machine (7).

Policies and rules are configuration, not code: adding a market/regulator or a rule should not
require a service release (D3 ADR-012).
