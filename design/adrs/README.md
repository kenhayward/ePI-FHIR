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
