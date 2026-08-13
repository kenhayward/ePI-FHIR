# config/ - Configuration as data

The extensibility hinge (capability 21): market/regulator definitions, lifecycle state models,
workflow definitions, terminology bindings, publishing routing, and event schemas - all as data,
so a new country, rule, or scheme is a configuration change, not a code release (D3 ADR-012).

Suggested layout:
- `markets/` - per-market/regulator configuration (active profile version, extensions, channels).
- `lifecycle/` - state models and allowed transitions.
- `workflows/` - review/approval workflow definitions.
- `bindings/` - element-to-value-set terminology bindings.

Configuration is versioned with effective dates, validated before activation, and promoted
through environments under GxP/CSV controlled release (D3 Section 10.3).
