# ADR-015: Identifier and versioning scheme

Status: accepted
Date: 2026-08-13

Realises CAP-SCM-007, and underpins CAP-SCM-005, CAP-LCM-002, CAP-LCM-006, CAP-LCM-011,
CAP-CHG-006. Required by iteration 1 (`design/iteration-1.md` Section 9).

## Context

The Deliverables Definition Section 6 lists the identifier and versioning scheme among the
decisions "cheap to make now and expensive to retrofit once data, integrations and content
exist". Once identifiers are minted they are permanent: they appear in cross-references, in
audit records, in published content, and in downstream consumers' systems. There is no
migration path that preserves traceability if the scheme is wrong.

Four forces shape the choice:

- **Reversibility of the FHIR server.** ADR-003 selects HAPI FHIR but is marked "subject to
  mandate", and every component in the Deliverables Definition Section 11 table is still
  unconfirmed. Identity must survive a change of FHIR server.
- **Variants have independent lives.** A label exists per market and per language (capability 9),
  and per-market regulatory-approval state is modelled separately from internal lifecycle state
  (ADR-005). Two markets can hold different approved content simultaneously.
- **Change impact is section-level.** Capability 8 computes impact sets across the content and
  reuse graph, and CAP-CHG-006 requires each affected section to trace to its source change.
  Sections must therefore be addressable in their own right, stably, across versions and
  translations.
- **Reconstruction is a regulatory obligation.** CAP-LCM-006 and CAP-AUD-004 require the full
  content and metadata of any historical version to be reproducible.

## Decision

**1. Identity is a business identifier the platform mints. The FHIR server's logical id is not
identity.** `Resource.id` is server-assigned and environment-specific; it differs between dev,
test, and production, and between server products. Identity is carried in `Bundle.identifier`
and `Composition.identifier` (a `system` plus `value` pair) and is what every reference, audit
record, and published artefact quotes.

**2. Four things are identified**, each with its own identifier:

| Thing | What it identifies | Stable across |
|---|---|---|
| Label family | A product's label of a given type, across all markets and languages | markets, languages, versions |
| Document | One market-and-language variant of a label family | its own versions |
| Section | A section within a document | versions and translations of that document |
| Reusable content unit | A shared block referenced by many documents | referencing documents, versions |

**3. Identifiers are opaque UUIDs (version 7) in a namespaced system URI. No business meaning is
encoded in an identifier.** Product code, market, language, and label type are searchable
metadata, never identifier substrings. UUIDv7 is time-ordered, which keeps index locality
without leaking meaning, and is available directly in .NET 9.

**4. A document version is a monotonic integer, starting at 1, over the document identity.**
Each version is an immutable snapshot. Version numbers are never reused, never renumbered, and
carry no compatibility semantics. A version is referenced as `{document-identifier}@{version}`.

**5. Market and language variants are separate document identities**, each with its own version
sequence, linked to their label family and to the document they were derived from. They are not
versions of a single document.

**6. Section identifiers are assigned on creation and preserved thereafter** - through editing,
through new versions, and through translation. A translated section carries the same section
identifier as its source, so a source change propagates to the right target section.

**7. References to reusable content units always carry the unit version**, per ADR-007's
pinned-by-default resolution. An unversioned reference to a reusable unit is invalid.

**8. An approved version records the versions it was validated and built against**: the
conformance profile version (ADR-016), the template version (CAP-TPL-007), and the master-data
snapshot (CAP-MDM-009). Reconstruction means reproducing the content *and* the context that
made it valid.

## Alternatives considered

- **Use the FHIR server's logical id as identity.** Simplest, and idiomatic within one server.
  Rejected: it is assigned by the server, so it differs across environments and would change
  under a mandated server swap (ADR-003), taking every stored cross-reference with it. It also
  makes migration (capability 4) and republication non-idempotent.
- **Encode business meaning in identifiers**, for example
  `urn:epi:doc:{product}:{type}:{market}:{language}`. Attractive for debugging and human
  readability. Rejected: every encoded fact is mutable. Products get recoded, markets get
  reorganised, a label is created against the wrong market and corrected. Each correction would
  force either a new identity (breaking traceability) or an identifier that lies about its
  content. Readability is better served by search.
- **Semantic versioning (major.minor.patch) for documents.** Rejected: semver communicates
  API compatibility, which has no meaning for regulated label content. What the domain needs is
  an ordered, immutable sequence plus explicit supersession edges, which integers state
  honestly. A human-facing display version, if a market requires one, is presentation metadata
  derived per market, not identity.
- **One document identity spanning all markets and languages, with variants as versions or
  branches.** Rejected: it makes ADR-005 unrepresentable. Two markets holding different approved
  content at the same time cannot be expressed as points on one version sequence.
- **UUIDv4 rather than v7.** Acceptable, and the difference is not architectural. v7 is chosen
  for time-ordering; nothing depends on it.

## Consequences

- Iteration 1 implements minting and immutability directly: FN-CC-002 (assign identifier),
  FN-CC-003 (version snapshot and lineage), FN-CC-007 (reject mutation of an existing version).
- **The identifier authority base URI must be fixed before any data exists outside a development
  environment.** Identifiers are permanent, so this cannot be deferred past the first shared
  environment. It is recorded as an open point below.
- Migration (capability 4) records legacy identifiers as *secondary* identifiers plus
  `Provenance` to the source artefact. A legacy identifier never becomes the identity of a
  migrated label.
- Sections must be assigned identifiers at creation, including sections created by template
  instantiation (capability 3) and by conversion (capability 4). A section without an identifier
  cannot participate in cross-references or impact analysis.
- A mandated change of FHIR server re-homes resources without changing identity, which keeps
  ADR-003 genuinely reversible.
- Downstream consumers (the option-A boundary) can rely on identifiers being stable and
  meaningless, so they must not parse them.

## Open points

- **The identifier authority.** The `system` URI for each identifier type needs a real
  authority, for example `https://epi.<organisation>/identifier/document`. The organisation and
  domain are not settled in the specifications. Blocking before the first shared environment,
  not before iteration 1: development data is disposable.
