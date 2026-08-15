# Iteration 3 - One Label, Many Forms

**Status:** Proposed v0.1, **Date:** 2026-08-14, **Audience:** Internal engineering
**Companion:** [iteration-1.md](iteration-1.md), [iteration-2.md](iteration-2.md),
D1 Solution Overview (Section 11 roadmap), D2.1-D2.6 Capability Specifications,
D3 Technical Architecture.

---

## 1. Purpose

Iteration 1 built a walking skeleton. Iteration 2 gave it governance: a label can now be
drafted from a template, moved through states nobody may skip, approved by someone who did not
write it under an electronic signature over the exact bytes, approved separately by each
market, found by whoever is allowed to see it, and reconstructed years later with a record of
what it was approved against.

**Everything so far concerns one label, in one language, at one moment.** That is not what a
regulated label is. A marketing authorisation covers many markets in many languages; the same
warning appears in a hundred products; a version approved today comes into force next month and
supersedes one that must remain retrievable; and none of it is any use to a patient until it
can be read as a leaflet rather than as a FHIR Bundle.

Iteration 3 is where the content model has to stand up to that. It is the increment that turns
a governed document into a governed *body of labelling*, and it opens the parts of the model -
reuse, cross-references, language variants - that D1 Section 6 identified as expensive to
retrofit and that both previous iterations deliberately deferred.

## 2. Where iteration 2 left things

### 2.1 What exists

| Capability | State |
|---|---|
| 2 Structured content model | Canonical Bundle, minted identity, immutable versions, section identity stable across versions, round-trip fidelity |
| 3 Templates | Instantiation of a conformant draft recording its template version (ADR-021). No library, no template lifecycle, no variants |
| 7 Lifecycle | Configurable state model, permitted transitions, per-market approval held separately (ADR-005, ADR-019). **No effective dating, no supersession** |
| 11 Validation | Structural conformance and reference integrity at the write gate against the pinned IG |
| 15 Search | Permission-scoped search as a query predicate, current-approved per market, retrieval of a specific version (ADR-022) |
| 16 Workflow | The approval gate: segregation of duties enforced on every route, signature required. **No routing, no assignment, no escalation** |
| 17 IAM | OIDC bearer authentication, OPA decisions, affiliate and market scope on every operation, permitted-scope resolution |
| 19 Audit and e-signature | Append-only audit, signature manifest over a content hash (ADR-020), full reconstruction with the validating context pinned at approval (ADR-023) |
| 20 Events | Emission to Kafka, keyed and ordered per document |
| 21 Configuration | Markets, profile bindings, identifier authority, state models - all config-as-data |

Thirty-two requirements carry evidence, twenty-three ADRs are accepted, and the whole governed
flow runs end to end against the development stack.

### 2.2 What iteration 2 planned and did not deliver

Stated plainly, because an iteration that closes its acceptance criteria while quietly dropping
scope has not finished - it has redefined finishing.

| Planned | Where | Reality |
|---|---|---|
| Effective dating and supersession (CAP-LCM-004, CAP-LCM-005) | iteration-2 Section 4.1, delivery row 9 | Not built. Carried into this iteration, where publishing and current-approved resolution both need it |
| Workflow routing (CAP-WFL-001) | iteration-2 Section 4.1; the only iteration-2 requirement still `planned` in the delivery map | Not built. The approval *gate* exists; routing a review to a named person does not |
| Master-data and terminology seams (capabilities 5 and 6) | iteration-2 Section 4.1 | Not built. Deferred for a third time, which is now the oldest debt in the plan |
| The thin authoring surface | iteration-2 Section 8.1 | Not built. Deferred deliberately - see Section 4.3 |

### 2.3 Debts carried into this iteration

Every open debt from [iteration-2.md](iteration-2.md) Section 2.2, with a home in this
iteration or a stated reason for waiting. Nothing is dropped by omission.

**Paid in this iteration:**

| Debt | Where recorded | Why here |
|---|---|---|
| Reusable content units and cross-references (CAP-SCM-004, CAP-SCM-005) | iteration-1 Section 4.2 | Section 4.1: the model piece this iteration exists to open, and ADR-007 already commits the resolution policy |
| Tags assert code systems; whether they should be published `CodeSystem` resources or governed extensions | ADR-017 | Recorded as "iteration 3, when the content model is next opened". It is being opened |
| `Bundle.identifier` is 0..1, so a submitter's identifier has no home as a secondary identifier | PR 5 review notes | Reuse and translation both create content that arrives with an identifier of its own. An ADR, and now rather than at ingestion |
| Lifecycle registration is not transactional with the content write, and the pinned validating context is written after the approval transition rather than with it | PR 12 review notes, ADR-023 | Marked "before any demonstration"; the demonstration exists. One transaction boundary serves both, and it is the first pull request of the iteration |
| `InitialiseAsync` is a bootstrap, not a migration - `CREATE TABLE IF NOT EXISTS` does nothing to a table that already exists, and CI cannot see the difference because it starts empty | PR 41 | It has bitten once. This iteration adds columns to every governance table, so it will bite again |
| Search parameter `effective date` matches nothing because effective dating does not exist | PR 40 | Effective dating arrives here, so the parameter becomes real |
| Snowstorm serves FHIR R4 while the platform is pinned to R5 (ADR-016) | PR 32 | Recorded as "before capability 6 work starts". Capability 6's binding points are in scope, so it is settled first, as an amendment to ADR-016 |
| "Which labels use unit X" is answerable only by reading every label: the references are recorded but nothing indexes them, and change impact needs the reverse direction | ADR-026 | With the search projection, and before change impact (capability 8) needs it |
| Cross-reference anchors are written by whatever produces the content, because there is no authoring surface to insert them. An author writing them by hand would be writing section identifiers by hand | ADR-028 | With the authoring surface, iteration 4 |
| "Which version is in force here" is a scan of a version's market history on every ask. It is the obvious thing to project into the search index, and doing so is a projection change rather than a model one | ADR-029 | When in-force is queried at volume, or with publishing (capability 14) |
| A migrated approval will have taken effect before the row recording it was written, which ADR-029 decision 5 refuses. The constraint has to hold against the original approval date rather than the date of the write | ADR-029 | With migration (capability 4) |
| ~~Tasks are held in memory only~~ - **paid**, PR 53: durable, migrated and held to the same conformance suite as the in-memory store | ADR-031, PR 52 | Done |
| Routing is one rule per state, so CAP-WFL-001's "multi-step, per market and label type" is not met: the shipped model has a single review step and nothing selects a different model per market | ADR-031, PR 53 | Delivery row 7c |
| Nothing tells anyone a task exists. Routing records the ask; carrying it is capability 20's, and that seam is not wired | ADR-031 | With notifications |
| The Firely SDK expands the core specification into one cache directory under the temporary path, and nothing outside this repository serialises it. A lock file closes it for this platform's own construction path; any other code calling `ZipSource.CreateValidationSource` directly would race again | PR 53 | Watch. If it recurs, the answer is a cache directory per process rather than a lock |
| Configuration paths that differ only inside a container have now caused three defects, each found by the walkthrough and invisible to CI. A start-up check naming every configuration file the platform expects, refusing to run without them rather than treating absence as a default, would turn all three into a failure to start | PR 53 | Before a deployment anyone relies on |
| Section-level translation status (CAP-LOC-005) is possible because section identity is shared between a variant and its source, and is not built: staleness is answered per variant rather than per section | ADR-032, PR 55 | With change impact (capability 8), which is what needs the granularity |
| A renderer upgrade may change output for the same inputs, which is a change to a template's toolchain rather than to the template. Storing the rendered bytes makes the artefact the evidence, and the toolchain version belongs in the same record as the template version | ADR-033, PR 56 | With the print engine, delivery row 10b |
| Inert registrations accumulate at whatever rate a content write fails after its registration, and nothing surfaces them | ADR-025 | Delivery row 1c. They are harmless individually and invisible in aggregate, which is the wrong pair of properties to leave together |
| Terminology versions are absent from the pinned validating context | ADR-023 | With the terminology binding points. A context that omits the terminology a version was validated against is incomplete in exactly the way ADR-023 exists to prevent |
| Validation is serialised outright, a correctness-first stopgap | PR 6a | Rendering and translation multiply validations per label by the number of languages. Measured here, and fixed if the measurement says so |

**Deliberately still waiting:**

| Debt | Where recorded | Home |
|---|---|---|
| The search projection has no rebuild path | PR 40 | When the projection moves out of process. Cheap now and cheaper than the alternative later, so build it if the iteration has room |
| Permitted scopes cost one policy call per candidate scope per request | ADR-022 | When search is measured, or when a caller with wide scope appears. Partial evaluation is the named production path |
| Search parameter `product` binds to what the content names as its subject | PR 40 | With capability 5, when there is master data to bind to |
| The EMRN EU IG is a preview release and absent from the package registry | ADR-016 | P2, before capability 10 (regulatory mapping), which is not in this iteration |
| Image tags pinned independently in three places | PR 4b, PR 6a | Housekeeping, any iteration |
| Capabilities 5 and 6 deferred despite being P0 | iteration-1 Section 4.3 | **Partly paid here** - the binding points, not the integrations. Which terminology sources the platform actually needs is reopened in Section 8 question 3, so the binding points are built source-agnostic and the integrations wait on that answer |

## 3. The principle for this iteration

Iteration 2's measure was that every governance question has an answer the platform can
produce. **Iteration 3's measure is that the same content can take every form a regulated
label has to take without ever being copied.**

Copying is the failure mode this iteration exists to prevent, and it is how most labelling
systems actually work: a warning is duplicated into forty products, a translation is a separate
document with no link to its source, a rendered PDF is filed somewhere and diverges from the
content it came from. Every one of those looks like it works until a safety change has to be
propagated, and then nobody can say what is affected.

So: one unit, referenced; one source version, translated; one approved version, rendered
deterministically. Where the platform must copy, it must record what it copied and from which
version.

## 4. Scope

### 4.1 In scope

| Capability | What this iteration builds |
|---|---|
| **2 Reuse and cross-references** | Reusable content units as first-class versioned resources; a label references a unit rather than containing it; pinned resolution by default with opt-in track-latest (ADR-007); cross-references between sections and between documents; resolution at read time with the pinned version, never the latest |
| **7 Effective dating and supersession** | When an approved version comes into force; supersession of a prior approved version; withdrawal; "which version is in force in this market on this date" as a first-class query, distinct from "which version is approved" |
| **9 Localisation and translation** | Market variants modelled as country x language x regulator against a source label (CAP-LOC-001); section-level translation preserving structure (CAP-LOC-003); translation status per variant and section, marked stale when the source moves (CAP-LOC-005); linguistic review routed and signed, with translator and approver segregated (CAP-LOC-007) |
| **13 Rendering** | Structured HTML and a rendered PDF (CAP-RND-001, CAP-RND-002), deterministic for a given label version *and* render-template version (CAP-RND-007, ADR-010); output stored in the asset store, never in the FHIR core; the rendered/artwork lineage enforced by type, not by convention |
| **16 Workflow routing** | Review and approval routed to a person or a role, assignment and reassignment, and the escalation seam - the half of capability 16 iteration 2 did not build (CAP-WFL-001) |
| **5, 6 binding points** | Master-data reference fields and terminology binding points on the content model, and terminology version recorded in the pinned validating context. Not the upstream integrations |

### 4.2 Out of scope

Publishing and distribution (14); change and impact analysis (8) beyond the events reuse
already emits; regulatory mapping and per-market profiles (10); compliance and completeness
checking (12); ingestion and import (1); migration (4); retention (22); reporting (23).

The template library's own lifecycle (CAP-TPL-001, CAP-TPL-008) and variant templates
(CAP-TPL-006) stay out unless Section 8's first question is answered otherwise - they are the
natural companion to reuse, and including them would make this iteration two iterations.

### 4.3 The deliberate deviation, again

**The authoring surface is deferred a second time,** and the argument has to be better than
last time because deferring twice is how a platform ends up with no users.

The case for building it now: a demonstration needs something to look at, and a regulatory
audience reads a screen more readily than a JSON payload.

The case against, which wins: **rendering gives them something to look at that is worth more.**
A rendered leaflet is the artefact a regulatory affairs professional recognises, it is produced
from the governed content rather than alongside it, and it demonstrates the whole chain in one
image. An editor over a single-language label with no reuse would demonstrate a rich-text box.

The condition attached: the authoring surface is **iteration 4's first item**, not another open
question. If it slips again the reason must be that something changed, not that something else
was more interesting.

## 5. The slice

```
An approved warning exists as a reusable unit                    (2)
  -> forty labels reference it, pinned at the version approved   (2, ADR-007)
  -> a label version is approved and effective-dated             (7)
  -> it supersedes the prior version, which stays retrievable    (7)
  -> the English source is translated into three languages       (9)
  -> each translation is routed for linguistic review and signed (9, 16)
  -> a change to the source marks its translations out of date   (9)
  -> the in-force version renders to HTML and PDF                (13)
  -> the render records the label version and the template version (13)
```

Every step remains audited, scoped, and reconstructable, and nothing in it copies content.

## 6. Acceptance criteria

| # | Criterion | Requirement |
|---|---|---|
| 1 | A label referencing a reusable unit resolves to the pinned version, and a later version of that unit does not change it | CAP-SCM-004, ADR-007 |
| 2 | A unit set to track-latest does change, and the change is an explicit, audited propagation rather than a silent one | CAP-SCM-004, ADR-007 |
| 3 | A cross-reference resolves to the section it names, in the version that named it | CAP-SCM-005 |
| 4 | "Which version is in force in this market on this date" answers correctly for a date before, between and after two effective dates | CAP-LCM-004 |
| 5 | An approved version supersedes its predecessor, and the predecessor remains retrievable and reconstructable | CAP-LCM-005, CAP-LCM-006 |
| 6 | A variant names the source version it was translated from, and a new source version marks it stale without altering it | CAP-LOC-001, CAP-LOC-005 |
| 7 | Linguistic review is routed to someone other than the translator and captures a signature | CAP-WFL-001, CAP-LOC-002, CAP-LOC-007 |
| 8 | The same label version and render-template version produce byte-identical output twice | CAP-RND-007, CAP-RND-003 |
| 9 | A rendered PDF and an artwork PDF are distinguishable by type, and neither can be stored as the other | CAP-RND-002, D1 Section 3.3, D3 Section 3.2 |
| 10 | A coded element carries the value set it is bound to and the binding strength, and an approval pins the terminology version alongside the conformance packages | CAP-TRM-005, CAP-TRM-004, CAP-LCM-011 |

Criteria 1 and 2 are the pair to build first and hardest. Pinned-by-default resolution is the
difference between a content platform and a wiki, and the failure is silent: a system that
resolves to the latest version looks correct in every test written against a corpus that has
only one version of anything.

Criterion 9 restates an invariant that already exists in the specifications and nowhere in the
code, which is exactly how invariants are lost.

Criterion 10 exists because the capability 5 and 6 seams have now been dropped from three
iterations in a row, and each time they were in scope with nothing to fail if they were not
built. A criterion is what stops that happening a fourth time.

## 7. Decisions this iteration forces

Each becomes an ADR.

1. **Reusable unit representation and reference mechanism - settled by
   [ADR-026](adrs/0026-reusable-content-units.md).** A unit is content in the
   same shape and the same store as a label, so it inherits identity, immutable versions,
   lifecycle and approval unchanged. A label holds a reference by business identifier and
   version, and the stored version materialises the referenced text alongside it - so the
   stored document stays self-contained and conformant while still recording what it borrowed
   and from which version. Resolution happens once, at the write gate, not at read time: the
   delivery sequence is corrected to match.
2. **Secondary identifiers - settled by
   [ADR-027](adrs/0027-secondary-identifiers-and-platform-code-systems.md),
   together with decision 6.** They live on the anchoring `Composition`, because a legacy or
   submitter identifier identifies the thing the content is about in another system, while
   `Bundle.identifier` identifies this document as this platform holds it. Different
   assertions, which is why one slot ever felt like a shortage of space.
3. **Effective dating semantics - settled by
   [ADR-029](adrs/0029-effective-dating.md).** Per market, because a version
   approved in two markets has two approvals and nothing says they take effect on the same day.
   Derived at the moment of asking rather than stored, because a stored flag is correct when it
   is written and wrong from the first midnight afterwards with nothing noticing. And nothing
   happens when an effective date passes: a version becomes in force without a job, an event or
   a state change, which is what makes the derivation safe.
4. **Translation as a version relationship.** Whether a translation is a version of the label,
   a separate label linked to a source, or a variant dimension alongside version - and how
   "out of date because the source moved" is expressed without mutating the translation.
5. **Render determinism and storage.** What makes two renders identical, what a render-template
   version is, where output lives (asset store, WORM), and how the rendered/artwork distinction
   is enforced by the type system rather than by a naming convention. Extends ADR-010.
6. **Tags as code systems - settled by
   [ADR-027](adrs/0027-secondary-identifiers-and-platform-code-systems.md).**
   The platform does not publish `CodeSystem` resources for its own tags: their codes already
   have a governing source (markets in configuration, affiliates in the identity provider,
   templates and units as content), and a published copy would be a second definition that goes
   stale. Where a value set is needed it is generated from the governing source. Revisited when
   the platform publishes content outside itself, which is capability 14.
7. **Snowstorm's FHIR version.** Carried from PR 32: an amendment to ADR-016 recording whether
   an R4 terminology server against an R5 platform is acceptable, and why.

## 8. Open questions for the programme

1. **Is the template library in scope?** Reuse makes boilerplate-as-a-unit natural
   (CAP-TPL-011), which pulls the template library and its own lifecycle in behind it.
   Including it makes templates genuinely managed; excluding it keeps the iteration finishable.
2. **How many languages does the demonstration need? - answered: three.** Enough to illustrate
   the principle without bulk. The design must not assume three: nothing in the model, the
   workflow or the rendering may be sized to the demonstration's fixture count, because the
   difference between three and twenty-four is volume and the difference between three and
   *three hard-coded* is a rewrite.
3. **Which terminology does ePI labelling actually need? - reopened, and deliberately on
   hold.** The choice is not being made now, and acceptance criterion 10 stays open until it
   is. What matters meanwhile is that the binding points are built source-agnostic, so
   answering it later costs a configuration change rather than a migration. Iteration 2 asked
   whether a SNOMED CT licence was being pursued. The answer, so far, is that **SNOMED may be
   the wrong source for this domain** and the choice is to be reviewed rather than assumed.

   That is worth taking seriously rather than treating as a delay. D2.2 binds ePI content to
   several vocabularies, and the ones a leaflet actually leans on - **EDQM standard terms** for
   pharmaceutical dose forms, routes and containers, **UCUM** for units, **ISO 639/3166** for
   language and country, **MedDRA** for adverse reactions, and the EMA and FDA controlled
   vocabularies - are not SNOMED. SNOMED CT earns its place where clinical concepts are being
   *reasoned over*; much of what an ePI needs is a controlled list, and a licensed clinical
   ontology is a heavy and expensive way to hold one.

   The decision is deferred deliberately, not dropped. What matters for this iteration is that
   **the binding points must not assume a particular source**: an element binds to a value set
   and a binding strength (CAP-TRM-005), and which server resolves that value set is a
   configuration and an ADR-006 question, not a shape in the content model. Built that way,
   answering this question later costs a configuration change rather than a migration.

   ADR-006 names Snowstorm as the primary terminology server. It should be revisited alongside
   this, together with the R4-against-R5 note carried from PR 32.
4. **Who owns render templates? - answered: regulatory, so they are content.** They follow
   ADR-021's reasoning exactly: versioned, immutable per version, lifecycle-managed and
   approvable through the same engine labels use, not files an administrator edits under
   `config/`. A render template determines what a patient reads, so an approver signs for it.

   The consequence for decision 5 is concrete: the render-template version a render is keyed to
   is a content version with its own approval, and "which template version produced this
   output" is answerable the same way "which unit version did this label borrow" is.

## 9. Delivery sequence

Provisional, and dependent on Section 8. Each row is a pull request, test-first, reviewed.

| # | Pull request | Size |
|---|---|---|
| 1a | Governance schema migrations, and the transition-and-pin transaction | M |
| 1b | ADR and mechanism: register a version before its content is written, so a failure leaves an inert record rather than ungoverned content | M |
| 1c | A reconciliation report for inert registrations - registered versions with no content at any version | S |
| 2 | ADR: reusable unit representation; the unit as a versioned resource with its own lifecycle | L |
| 3 | Materialisation of referenced content at the write gate, and track-latest as an explicit, audited propagation | L |
| 4 | Cross-references between sections and documents, resolved within the version that names them | M |
| 5 | ADR and mechanism: effective dating, per market | M |
| 6 | Supersession and withdrawal, with the predecessor retrievable | M |
| 7a | Workflow routing: the model, config-as-data, and tasks raised and closed by transitions | M |
| 7b | The durable task store, and the API surface for what is waiting and reassigning it | M |
| 7c | Multi-step routing and per-market rules, which CAP-WFL-001 asks for and one review step does not give | M |
| 8 | ADR: translation as a version relationship; language variants linked to a source version | L |
| 9 | Linguistic review routed and signed; source change marks translations out of date | M |
| 10a | ADR and mechanism: deterministic rendering to HTML, keyed to a render-template version | M |
| 10b | PDF from that HTML through the print engine, normalising the two dates Chromium writes for itself (measured - see ADR-033) | M |
| 11 | Render storage in the asset store, with the rendered/artwork lineage enforced by type | M |
| 12 | Master-data and terminology binding points; terminology version in the pinned context; ADR-016 amendment | M |
| 13 | ADR: tags as code systems; secondary identifiers - **delivered early**, while the content model was open | S |

Rows 1a and 13 are deliberately unglamorous and deliberately early and late respectively: the
first is a debt that has been marked "before any demonstration" for two iterations, and the
last is cheap only while the content model is still open.

**Row 7 was split into 7a and 7b during delivery**, and 7a is the routing decision and
mechanism while 7b is durability and the surface. Recorded here rather than absorbed, as
Section 10 asks.

**Row 1 was split into 1a and 1b during delivery**, and is recorded here rather than absorbed
quietly. The two halves of that debt turn out to need different mechanisms: the transition and
its pinned context live in one database and can be one transaction, while registration crosses
the content store and the governance store and cannot. Section 10 asks for scope to be
delivered or explicitly renegotiated, and this is what that looks like.

## 10. Exit criteria

Iteration 3 is complete when all ten acceptance criteria in Section 6 pass in CI, the ADRs in
Section 7 are merged, the traceability matrices record evidence for every requirement scheduled
against iteration 3, and the debts in Section 2.3 marked as paid here are either paid or
re-recorded with a reason.

And one addition, learned from iteration 2: **the scope in Section 4.1 is either delivered or
explicitly renegotiated.** Closing the acceptance criteria while dropping planned capabilities
is how Section 2.2 came to exist, and an exit criterion that cannot see that is not an exit
criterion.

*End of Iteration 3 plan v0.1.*
