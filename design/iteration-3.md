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
| Snowstorm serves FHIR R4 while the platform is pinned to R5 (ADR-016) | PR 32 | **Still open, deliberately.** It is a property of one candidate terminology source, and choosing sources is the programme question being held. ADR-036 is written so the answer stays a component behind a port and a configuration entry |
| "Which labels use unit X" is answerable only by reading every label: the references are recorded but nothing indexes them, and change impact needs the reverse direction | ADR-026 | With the search projection, and before change impact (capability 8) needs it |
| Cross-reference anchors are written by whatever produces the content, because there is no authoring surface to insert them. An author writing them by hand would be writing section identifiers by hand | ADR-028 | With the authoring surface, iteration 4 |
| The product directory can answer and nothing on the write path asks it: `Composition.subject.display` is still what search indexes, because nothing writes a resolved product reference into content. The port exists, the binding of content to it does not | ADR-036 | With the authoring surface, which is where a product is chosen rather than typed |
| Terminology lookup is not implemented - the directory reports which versions answer and returns null for every code. Which server, and which source for which concept domain, is the programme question being held | ADR-036 | When the source question is answered. The change is then a directory implementation and a configuration entry |
| "Which version is in force here" is a scan of a version's market history on every ask. It is the obvious thing to project into the search index, and doing so is a projection change rather than a model one | ADR-029 | When in-force is queried at volume, or with publishing (capability 14) |
| A migrated approval will have taken effect before the row recording it was written, which ADR-029 decision 5 refuses. The constraint has to hold against the original approval date rather than the date of the write | ADR-029 | With migration (capability 4) |
| ~~Tasks are held in memory only~~ - **paid**, PR 53: durable, migrated and held to the same conformance suite as the in-memory store | ADR-031, PR 52 | Done |
| ~~Routing is one rule per state, and nothing selects a different model per market~~ - **paid**, PR 64: a catalogue selects by label type and market, and a state may ask several people at once. ADR-035 settles what "multi-step" means here - a sequence is states, because a step completing is a lifecycle transition (CAP-WFL-005), and simultaneous asks are rules | ADR-031, PR 53 | Done |
| Routing describes the process; the state model enforces it. A market whose routing asks for two steps and a state model that permits going straight to approval can still go straight to approval. Enforcing a genuinely different path per market needs per-label-type state models, and the platform has exactly one internal state model | ADR-035 | Watch. A larger decision than routing, and named in ADR-035 rather than left to be discovered |
| Conditional routing - CAP-WFL-006's third clause, where the path depends on the nature of the change rather than on the market - is not built and was not attempted | ADR-035 | With change classification (capability 8), which is what it would branch on |
| Nothing tells anyone a task exists. Routing records the ask; carrying it is capability 20's, and that seam is not wired | ADR-031 | With notifications |
| The Firely SDK expands the core specification into one cache directory under the temporary path, and nothing outside this repository serialises it. A lock file closes it for this platform's own construction path; any other code calling `ZipSource.CreateValidationSource` directly would race again | PR 53 | Watch. If it recurs, the answer is a cache directory per process rather than a lock |
| ~~Configuration paths that differ only inside a container have now caused three defects, each found by the walkthrough and invisible to CI~~ - **paid**, PR 65, in two halves. The platform resolves every configuration path at start-up and refuses to run without one, so the failure is attributable to the deployment that caused it. And `tools/verify-configuration-paths.py` cross-checks what the application reads, what the image declares and what the stack mounts - verified against all three historical defects, each of which it catches with the right message | PR 53 | Done |
| The vendored conformance packages are deliberately not resolved at start-up: a host that never validates or approves anything need not have them present. So a wrong `Epi:Validation:PackagesPath` is still found on first validation rather than at start-up - the CI guard covers the declared path, and nothing covers a path configured only at deployment | PR 65 | Watch. Requiring them would make the packages a start-up dependency of every deployment rather than of the work that uses them |
| Section-level translation status (CAP-LOC-005) is possible because section identity is shared between a variant and its source, and is not built: staleness is answered per variant rather than per section | ADR-032, PR 55 | With change impact (capability 8), which is what needs the granularity |
| A renderer upgrade may change output for the same inputs, which is a change to a template's toolchain rather than to the template. Storing the rendered bytes makes the artefact the evidence, and the toolchain version belongs in the same record as the template version | ADR-033, PR 56 | With the print engine, delivery row 10b |
| ~~Image pinning was checked by reading, which missed four tags across two pull requests~~ - **paid**, PR 59: a CI guard refuses any image that is not a digest, a version with a patch component or a dated release, unless it carries a stated reason. It catches all four historically | PR 38, PR 58 | Done |
| ~~The compose stack pinned Gotenberg to `:8`~~ - **paid**, PR 58: a major-version tag that moves, missed when PR 38 pinned every other image. Pinned to the version the stack is verified on | PR 58 | Done |
| ~~PDF determinism is proven against a stand-in rather than the engine~~ - **paid**, PR 60: IT-019 runs against a real Gotenberg. The host crash was a Testcontainers wait strategy inside an xUnit collection fixture, which kills the VSTest process with no message | PR 58 | Done |
| A Testcontainers wait strategy attached to a container built in an xUnit collection fixture crashes the VSTest host outright on 4.13.0 - no message, no exception, every test failing in about a millisecond. Worked around by polling after start; the other container fixtures use module builders and are unaffected, so nothing else needs changing today | PR 60 | Watch. Revisit on a Testcontainers upgrade |
| ~~Rendered output is not stored anywhere durable, and write-once comes from a check in application code rather than from object-lock~~ - **paid**, PR 62: the store runs on MinIO through the S3 API and answers the same conformance suite. The debt as written was also wrong, and ADR-034 records the measurement that corrects it: object-lock protects a *version*, not a *key*, so an unconditional overwrite of a retained object is accepted and becomes what a read returns. Write-once comes from a conditional write; object-lock stops the accepted version being destroyed afterwards | PR 61 | Done |
| The development stack created `epi-rendered`, `epi-artwork` and `epi-content` with object-lock enabled and no default retention, which enables the mechanism and then does not use it. Found by ADR-034's measurement and fixed in the same PR - recorded because a bucket in that state reads as protected at a glance and protects nothing | PR 62 | Done |
| The asset store lives in `Epi.Rendering` because rendering is what needed it first, and ingesting artwork is not rendering. Its own project is the honest boundary; the object-store client and retention configuration sit there too | PR 61, PR 62 | Watch. Cheap to move while there is one caller, and the reason to move it is a second one |
| ~~Inert registrations accumulate at whatever rate a content write fails after its registration, and nothing surfaces them~~ - **paid**, PR 63: the report, with a settle period so writes in flight are not reported as failures, and an endpoint restricted to a platform-wide role. It also names a consequence the debt did not: an inert registration permanently reserves a version number, because the store refuses a second registration and a retry therefore fails before it reaches the content store | ADR-025 | Done |
| Reconciliation cannot be permission-scoped, because scope is decided on the content and an inert registration has none. It is restricted by role instead, and the report names document identifiers across every affiliate to whoever holds that role | PR 63 | Watch. Right for an operational report; revisit if the role is ever handed out widely |
| Nothing runs the reconciliation on a schedule. The report exists and answers on request, so finding an inert registration still depends on somebody asking | PR 63 | With operational monitoring. A report nobody runs is the same as no report, one step later |
| ~~Terminology versions are absent from the pinned validating context~~ - **paid**, PR 67: `PinnedContext` carries the bindings in force at approval, written by the same transaction as the pin. An approval with none records an empty set, which is deliberately distinguishable from a pin taken before bindings existed | ADR-023 | Done |
| ~~Validation is serialised outright, a correctness-first stopgap~~ - **measured and settled**, PR 66. The measurement reversed the expected answer twice. The gate is load-bearing, not merely cautious: fifteen rounds of sixteen concurrent validations against a cold validator, reaching past it, reported errors in 238 of 240 - on a document that is valid. And the gate was not what cost anything: 32 concurrent validations take 175 ms with it and 39 ms without on dedicated threads, but 10.6 seconds through the thread pool, because a synchronous wait holds a pool thread and a starved pool injects replacements about twice a second. So the gate stays and stops blocking: the same 32 validations now take 74 ms rather than 12,778 ms | PR 6a | Done |
| The gate remains one process-wide semaphore, so validation throughput is one document at a time however many cores a deployment has. Warm concurrent validation showed no errors in 64 attempts, which is suggestive and nowhere near enough to act on - the failure it guards against rejects valid labels, and that asymmetry says measure much harder before relaxing it | PR 66 | When validation throughput is the constraint. Nothing yet says it is |

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
| 7c | Routing selected per label type and market, and several people asked at once; multi-step as states (ADR-035) | M |
| 8 | ADR: translation as a version relationship; language variants linked to a source version | L |
| 9 | Linguistic review routed and signed; source change marks translations out of date | M |
| 10a | ADR and mechanism: deterministic rendering to HTML, keyed to a render-template version | M |
| 10b | PDF from that HTML through the print engine, normalising the two dates Chromium writes for itself (measured - see ADR-033) | M |
| 11a | The asset store contract: keys, the two lineages, and write-once | M |
| 11b | The durable store on MinIO, held to the same conformance suite - conditional write for write-once, object-lock for immutability of what was written (ADR-034) | M |
| 12 | Master-data and terminology binding points; terminology version in the pinned context. The ADR-016 amendment is **not** delivered: it turns on the source question, which is held (ADR-036) | M |
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
