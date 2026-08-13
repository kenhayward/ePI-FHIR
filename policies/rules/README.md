# policies/rules/ - Business and compliance rules

Externalised rules (e.g. DMN decision tables, or a rules DSL) driving:
- validation severity and gating (capability 11),
- completeness and compliance checks, including CDS-origin (capability 12),
- lifecycle state transitions (capability 7).

Rules are versioned with effective dates and are configuration, not code (D3 ADR-012, capability 21).
Organise by market/regulator so a new scheme is added here rather than in service code.
