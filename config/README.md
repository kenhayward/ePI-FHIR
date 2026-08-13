# config/ - Configuration as data

The extensibility hinge (capability 21): market/regulator definitions, lifecycle state models,
workflow definitions, terminology bindings, publishing routing, and event schemas - all as data,
so a new country, rule, or scheme is a configuration change, not a code release (D3 ADR-012).

Layout:
- `identifiers.json` - the namespaces this deployment mints identifiers and tags into
  ([ADR-017](../design/adrs/0017-identifier-authority-as-configuration.md)). **An adopting
  organisation replaces these before storing anything they intend to keep**: identifiers are
  permanent and appear in stored content and audit records. The shipped values use a domain
  reserved for documentation, so an unset authority is conspicuous rather than plausible.
- `markets/` - per-market/regulator configuration. **Implemented**, see below.
- `lifecycle/` - state models and allowed transitions. Later iteration (capability 7).
- `workflows/` - review/approval workflow definitions. Later iteration (capability 16).
- `bindings/` - element-to-value-set terminology bindings. Later iteration (capability 6).

## markets/

One JSON file per market, loaded and validated by `MarketCatalogue` in `Epi.Governance`.
Adding a market is adding a file - no code change, no registration step (CAP-CFG-004).

```json
{
  "code": "GB",
  "name": "United Kingdom",
  "regulator": "MHRA",
  "languages": ["en-GB"],
  "affiliates": ["uk-affiliate"],
  "profile": {
    "package": "hl7.fhir.uv.emedicinal-product-info",
    "version": "1.0.0"
  }
}
```

| Field | Rule |
|---|---|
| `code` | Required, non-empty, unique across all files (compared case-insensitively) |
| `name` | Required, non-empty |
| `regulator` | Required, non-empty |
| `languages` | Required, at least one non-empty BCP-47 tag |
| `affiliates` | Required, at least one non-empty affiliate scope (consumed by capability 17) |
| `profile` | Required. The pinned conformance package and version this market's content is validated against (ADR-016 decision 7). A market with no profile has no yardstick, so it does not load |

Validation is strict and all-or-nothing: an unknown or mistyped property is an error rather
than being ignored, every problem in every file is reported together rather than only the
first, and one invalid file means no catalogue rather than a partial one (CAP-CFG-006).

Naming a package and version per market is what lets a market adopt a conformance release on
its own timetable, without a platform release. The packages themselves are vendored under
[profiles/packages/](../profiles/packages/README.md) and pinned by
[ADR-016](../design/adrs/0016-pinned-epi-ig-release-and-section-codes.md).

Configuration is versioned with effective dates, validated before activation, and promoted
through environments under GxP/CSV controlled release (D3 Section 10.3).
