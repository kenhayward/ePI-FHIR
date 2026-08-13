# Iteration 1 - Walking Skeleton

**Status:** Proposed v0.1, **Date:** 2026-08-13, **Audience:** Internal engineering
**Companion:** D1 Solution Overview (Section 11 roadmap), D2.1-D2.6 Capability Specifications,
D3 Technical Architecture, Deliverables Definition (Section 6 target-state decisions).

---

## 1. Purpose

The first buildable increment. It establishes the architectural seams that are cheap to
create now and expensive to retrofit, and proves them with one thin end-to-end thread
rather than one deeply-built capability.

## 2. The constraint that shapes this iteration

D1 Section 11 places seven capabilities in P0: 2 (structured content model), 5 (master data),
6 (terminology), 17 (IAM), 18 (security), 19 (audit), 21 (configuration) - "the validatable
spine". Built horizontally that is a long stretch of work before anything runs end to end.

The sharper guide is the Deliverables Definition Section 6, which lists the decisions that are
"cheap to make now and expensive to retrofit once data, integrations and content exist":
canonical model and reuse, the event backbone, config-as-data, multi-tenancy and partitioning,
the identifier and versioning scheme, regulatory-approval state versus internal lifecycle state,
CSV validatability, API versioning, and effective dating.

**Principle for this iteration: buy optionality on the expensive decisions, not features.**
A walking skeleton that exercises every seam thinly is worth more than any single capability
built deeply, because it is the seams that cannot be changed later.

## 3. The slice

One product, one market, one label document, no user interface. Every call authenticated,
authorised, scoped, and audited from the first commit.

```
POST /fhir/Bundle   (OIDC access token)
  -> authorise: OPA decision on affiliate and market scope        (17)
  -> structural validation against the pinned ePI profile         (11, minimal)
  -> store canonical content in HAPI FHIR R5                      (2)
  -> assign canonical identifier and immutable version            (2, CAP-SCM-007)
  -> write an append-only audit record with actor and before/after (19)
  -> emit content.ingested to the event backbone, no consumers    (20, emit only)

GET /fhir/Bundle/{id}
  -> returns content identical to what was submitted              (2, CAP-SCM-010)
```

## 4. Scope

### 4.1 In scope

| Capability | What this iteration builds |
|---|---|
| 2 Structured Content Model | Store and retrieve an ePI document `Bundle` anchored by a `Composition`; canonical identifiers; immutable version lineage; round-trip fidelity |
| 17 IAM | OIDC authentication via Keycloak; OPA authorisation on every call; affiliate and market scoping enforced at the data-access layer |
| 19 Audit | Append-only audit sink capturing actor, action, target, before/after, timestamp, for both content writes and access decisions |
| 21 Configuration | Market definitions as data; a market carries its active profile version and scoping attributes |
| 11 Validation | Structural well-formedness and reference integrity at the write gate only |
| 20 Events | Emission of content events to Kafka; no consumers, no subscriptions |
| 18 Security | Dev-level TLS and secrets handling; dependency and container scanning already enforced in CI |

### 4.2 Out of scope

Authoring UI; templates (3); lifecycle states and workflow (7, 16); electronic signature (19,
signature half); rendering (13); publishing (14); localisation and translation (9); change and
impact analysis (8); migration (4); search beyond FHIR search (15); reusable content units and
cross-references (2, later); terminology-bound validation (6); compliance checking (12);
SPOR and master-data integration (5).

### 4.3 Deliberate deviations from a literal P0

Two deviations, both argued rather than assumed. Either can be vetoed without disturbing the
rest of the iteration.

- **Capabilities 5 and 6 are deferred despite being P0.** They are P0 because templates,
  authoring, and validation depend on them, and this iteration builds none of those. It lands
  a seam only: a product-identifier reference field on the content model, and section codes
  bound to a small locally-held `ValueSet`. Full master data (SPOR) and terminology (Snowstorm)
  arrive in iteration 2. Note that Snowstorm additionally requires a SNOMED CT licence, so the
  procurement path should start now rather than block iteration 2.
- **Capability 20 is included though it is P2.** Emission only. The Deliverables Definition
  Section 6 warns that "retrofitting events onto a request/response core is costly"; emitting
  from day one is nearly free and preserves the option.

A minimal write API is also required, ahead of the ingestion capability (1, P1). It is justified
by CAP-SCM-009 and the content read/write APIs that capability 2 already exposes to 1, 3, 11,
and 13. It is not ingestion: no batch, no artwork classification, no source-document conversion,
no deduplication.

## 5. The boundary that matters most

**Version semantics (capability 2) and lifecycle state (capability 7) are different things.**
This iteration builds immutable version lineage with stable identifiers, and no draft, in-review,
or approved states whatsoever. Lifecycle, workflow, and electronic signature arrive together in
iteration 2, because an approval gate without segregation of duties and signature capture is
worse than no approval gate: it looks like a control and is not one.

## 6. Acceptance criteria

Drawn from the capability specifications, and the definition of done for the iteration.

| # | Criterion | Requirement |
|---|---|---|
| 1 | A conformant ePI Bundle round-trips with no content loss | CAP-SCM-010 |
| 2 | A user outside the resource's affiliate or market is denied | CAP-IAM-002, CAP-IAM-007 |
| 3 | Every content write and every access decision produces an immutable audit record with actor, timestamp, and before/after values | CAP-AUD-001, CAP-AUD-002 |
| 4 | A second market is added by configuration alone, with no code change | CAP-CFG-004 |
| 5 | Malformed content is rejected with itemised, located errors and no partial state | CAP-VAL-003, CAP-VAL-005 |
| 6 | An attempt to mutate an existing version is rejected; history is reconstructable | CAP-SCM-007 |

Criterion 1 is the one that validates the canonical-model premise on which the whole platform
rests. Criterion 2 is the one that is unrecoverable if deferred: retrofitting tenancy after data
exists is the classic failure of this system shape.

## 7. Traceability

Requirements trace to delivery mechanically rather than by hand, as a **V model**: requirement ->
design function -> unit test on the way down and back up, and requirement -> integration test
across the top. See `design/traceability/` for the registries and the generated matrices.

This iteration declares 23 design functions and 9 integration-test scenarios against its 15
scheduled requirements, before any of them exist in code. Under test-driven development that
ordering is the point: the trace is written first, the gap report in `v-model-trace.md` lists
everything not yet built, and the gaps close as tests land.

- Unit tests are named for the design function they verify, for example
  `FN_CC_006_serialises_without_content_loss`.
- Integration tests are named for the scenario, for example
  `IT_001_bundle_round_trips_through_create_and_read`.
- Both are **discovered from the code** by `tools/build-traceability.py`, so no matrix has to be
  updated by hand after writing a test, and CI fails if a generated matrix is stale or a test
  names something that does not exist.

This is the Deliverables Definition Section 8 cross-cutting artefact, and the spine of the
GxP/CSV chain in D3 Section 8.4. It costs almost nothing now and cannot reasonably be
reconstructed across hundreds of tests later.

## 8. Solution shape

A modular monolith, decomposable along the D2 seams, per D3 Section 1.4. One .NET solution
under `src/`:

| Project | Responsibility |
|---|---|
| `Epi.Api` | HTTP host, OIDC authentication, OPA authorisation middleware, API versioning |
| `Epi.ContentCore` | Capability 2: content model, canonical identifiers, FHIR client |
| `Epi.Governance` | Capability 19 audit sink and capability 21 configuration loader |
| `Epi.Contracts` | Event schemas and API DTOs |
| `*.Tests` | Unit tests per project, plus API integration tests |

Content is accessed through the **FHIR REST API** using the Firely .NET SDK (BSD-3-Clause, so
compatible with this repository's Apache-2.0 licence). No HAPI-specific features are used, which
keeps ADR-003 genuinely reversible (see Section 10).

## 9. Decisions this iteration forces

Each becomes an ADR in `design/adrs/`:

1. **[ADR-015 Identifier and versioning scheme](adrs/0015-identifier-and-versioning-scheme.md)** -
   globally stable identifiers and version semantics for documents, sections, and reusable units
   across languages and markets. The most consequential artefact of this iteration; it underpins
   traceability, cross-references, and supersession. Delivered in PR 2.
2. **[ADR-016 Pinned ePI IG release and section code systems](adrs/0016-pinned-epi-ig-release-and-section-codes.md)** -
   D3 Section 15 open item 3. Validation has no yardstick until this is fixed. Delivered in PR 2;
   the exact package version string is confirmed before PR 5.
3. **Audit event contract** - one uniform event shape that every capability writes, per the D2.5
   cross-capability note on keeping the audit contract uniform. Due with PR 7.

## 10. Risks and open items

- **Every component in the Deliverables Definition Section 11 mandated-vs-open table is still
  `_tbc_`, including the FHIR server.** D3 marks ADR-003 (HAPI FHIR) "subject to mandate".
  Mitigation: this iteration codes against the FHIR REST API only, so a mandated Firely Server
  or Azure Health Data Services becomes a configuration change rather than a rewrite. Confirm
  the table before iteration 2, when master data and terminology bind to real products.
- **Capability 15 (Search, Access & Retrieval) phasing - resolved.** D1 Section 11 omitted it
  while D2.5 already assigned it P1: an inconsistency between documents rather than an open
  decision. D1 has been corrected to agree. Capability 15 is **P1** (its dependencies - 2, 7, 17,
  18, 19 - are satisfied by then, content first exists at volume in P1, and the permission-scoping
  in CAP-SCH-004 must not be retrofitted), extending into **P2** for the consumer-read
  requirements CAP-SCH-005 and CAP-SCH-007, which depend on publishing (14) and rendering (13).
  Nothing in capability 15 is built in this iteration, but the authorisation layer built here is
  what lets P1 search be scoped by construction rather than retrofitted.
- **NFRs are unquantified** (D3 Section 15 open item 2). This iteration sets no performance
  budget; it should establish the measurement path so budgets can be set against evidence.
- **Data residency map is unconfirmed** (D3 Section 15 open item 3). Logical scoping is built
  here; physical partitioning is not, and ADR-004 allows for it later.

## 11. Delivery sequence

Each row is one pull request, test-first, reviewed and merged before the next begins.

| # | Pull request | Size |
|---|---|---|
| 1 | .NET solution skeleton, health endpoint, `dotnet` CI job activates | S |
| 2 | ADRs: identifier and versioning scheme; pinned IG release | S |
| 3 | Configuration-as-data loader and the first market definition | M |
| 4 | Content core: create and read a Bundle, round-trip fidelity | L |
| 5 | Structural validation at the write gate | M |
| 6 | OIDC authentication and OPA authorisation with affiliate and market scoping | L |
| 7 | Append-only audit sink | M |
| 8 | Content event emission | S |
| 9 | Application service added to the Docker Compose dev stack | S |

PR 1 makes the `dotnet` job live, at which point it should be added to the branch-protection
ruleset's required checks. The `tdd-guard` job begins to apply from PR 3 onward, which is its
intent.

## 12. Exit criteria

Iteration 1 is complete when all six acceptance criteria in Section 6 pass in CI, the three ADRs
in Section 9 are merged, and the traceability matrix records delivery evidence for every
requirement listed against iteration 1 in `delivery-map.json`.

*End of Iteration 1 plan v0.1.*
