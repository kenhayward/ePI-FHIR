# Architecture Decision Records (ADRs)

Significant decisions are recorded as ADRs, using the convention `NNNN-title.md`.

**Where to look.** ADR-001 to ADR-014 are summarised inline in
`design/D3-technical-architecture.md` Section 14; they were taken as a set while the
architecture was being written. **ADR-015 onward are individual records in this directory**, one
file each, and D3 Section 14 links to them. Splitting 001-014 into files is worthwhile when one
of them next needs revising, not as a bulk exercise.

## Records

| ADR | Decision | Status |
|---|---|---|
| [ADR-015](0015-identifier-and-versioning-scheme.md) | Identifier and versioning scheme | accepted |
| [ADR-016](0016-pinned-epi-ig-release-and-section-codes.md) | Pinned ePI IG release and section code systems | accepted |
| [ADR-017](0017-identifier-authority-as-configuration.md) | Identifier authority as configuration | accepted |
| [ADR-018](0018-audit-event-contract.md) | Audit event contract | accepted |
| [ADR-019](0019-lifecycle-state-model.md) | Lifecycle state model | accepted |
| [ADR-020](0020-electronic-signature.md) | Electronic signature | accepted |
| [ADR-021](0021-template-representation.md) | Template representation | accepted |
| [ADR-022](0022-permission-scoped-search.md) | Permission-scoped search and current-approved retrieval | accepted |
| [ADR-023](0023-historical-version-reconstruction.md) | Reconstructing a historical version | accepted |
| [ADR-024](0024-atomic-governance-writes.md) | Atomic governance writes, and a migrated schema to hold them | accepted |
| [ADR-025](0025-register-before-write.md) | A version is registered before its content is written | accepted |
| [ADR-026](0026-reusable-content-units.md) | Reusable content units, and how a label refers to one | accepted |
| [ADR-027](0027-secondary-identifiers-and-platform-code-systems.md) | Secondary identifiers, and the platform's own code systems | accepted |
| [ADR-028](0028-cross-references.md) | Cross-references between sections | accepted |
| [ADR-029](0029-effective-dating.md) | Effective dating, per market | accepted |
| [ADR-030](0030-supersession.md) | Supersession | accepted |

A record stays `proposed` until the pull request carrying it is merged, at which point it
becomes `accepted`. Superseding a decision means a new record that says so, never editing the
old one: the point of an ADR is that it preserves the reasoning available at the time.

Suggested ADR template:

```
# ADR-NNNN: <title>
Status: proposed | accepted | superseded by ADR-XXXX
Date: YYYY-MM-DD

## Context
## Decision
## Alternatives considered
## Consequences
```
