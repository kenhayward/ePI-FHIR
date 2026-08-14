# Iteration 2 - A Label With A Lifecycle

**Status:** Proposed v0.1, **Date:** 2026-08-13, **Audience:** Internal engineering
**Companion:** [iteration-1.md](iteration-1.md), D1 Solution Overview (Section 11 roadmap),
D2.1-D2.6 Capability Specifications, D3 Technical Architecture.

---

## 1. Purpose

Iteration 1 built a walking skeleton: one document, stored canonically, authorised, validated,
audited, and announced. It has no lifecycle. Content can be created and versioned, and that is
all - there is no draft, no review, no approval, and nothing that distinguishes a work in
progress from a label anyone should rely on.

Iteration 2 gives content a lifecycle and the governance around it. That is the increment that
turns a content store into an ePI management system, and it is the part a regulated
organisation will judge the approach on.

## 2. Where iteration 1 left things

### 2.1 What exists

| Capability | State |
|---|---|
| 2 Structured content model | Canonical Bundle, minted identity, immutable versions, round-trip fidelity, against HAPI |
| 11 Validation | Structural conformance and reference integrity at the write gate, against the pinned IG |
| 17 IAM | OIDC bearer authentication, OPA decisions, affiliate and market scope on every operation |
| 19 Audit | Append-only in PostgreSQL, enforced by the database, recording writes and access decisions |
| 20 Events | Emission to Kafka, keyed and ordered per document |
| 21 Configuration | Markets, profile bindings, identifier authority - all config-as-data |

### 2.2 Debts recorded during iteration 1

These are carried here deliberately rather than discovered again later. Each was written down
when it was found, and each has a natural home in this iteration or a named later one.

| Debt | Where recorded | Proposed home |
|---|---|---|
| Sections carry no identity, though ADR-015 decision 6 requires it | ADR-015 | **Iteration 2** - change impact and cross-references depend on it |
| `Bundle.identifier` is 0..1, so a submitter's own identifier is overwritten with no home for it as a secondary identifier | PR 5 review notes | **Iteration 2** - an ADR, before ingestion (capability 1) makes it acute |
| Validation is serialised outright, a correctness-first stopgap | PR 6a | **Iteration 2** - revisit before throughput matters |
| Capabilities 5 and 6 deferred despite being P0 | iteration-1 Section 4.3 | **Iteration 2** for the seams; Snowstorm needs a SNOMED licence, so start procurement now |
| Reusable content units and cross-references (CAP-SCM-004, CAP-SCM-005) | iteration-1 Section 4.2 | **Iteration 2** if reuse is in scope, otherwise iteration 3 |
| Tags assert code systems; whether they should be published `CodeSystem` resources or governed extensions | ADR-017 | Iteration 3, when the content model is next opened |
| The EMRN EU IG is a preview release and absent from the package registry | ADR-016 | P2, before capability 10 |
| Image tags pinned independently in three places | PR 4b, PR 6a | Housekeeping, any iteration |
| ~~The compose stack has never been run end to end outside CI~~ - **paid**, PR 32: three faults found, all invisible to a `docker compose config` check | PR 9b | Done |
| ~~Nine dev-stack images run on `:latest`~~ - **paid**, PR 38: every image pinned, and the whole stack including all three optional profiles verified running on the pins | PR 32 | Done |
| Snowstorm serves FHIR R4 (`4.0.1`) while the platform is pinned to R5 (ADR-016). Terminology operations are largely version-agnostic in practice, so this may be a non-issue - but it should be a recorded decision rather than a surprise | PR 32 | **Before capability 6 work starts** - an ADR note, or an amendment to ADR-016 |
| ~~The durable stores are built and proven but nothing composes them~~ - **paid**, PR 34 | PR 10 review notes | Done |
| Lifecycle registration is not transactional with the content write. If the write succeeds and registration fails there is content nobody is recorded as having authored, and the author is what segregation of duties is checked against. Narrower than registering later, and still real | PR 12 review notes | **Before any demonstration** - needs a transaction across two stores, or a reconciling read that registers what it finds unmanaged |
| ~~The delivery map carried duplicate keys, so an entry could be written, reviewed and merged without ever reaching a matrix~~ - **paid**, PR 40: the registry loader refuses duplicates. CAP-TPL-004 and CAP-TPL-007 had been recorded as `planned` for a release despite PR 14 recording evidence for them | PR 40 | Done |
| The search projection has no rebuild path. It is derived and therefore rebuildable in principle, and nothing implements it - which means a projection lost or corrupted cannot be restored without replaying writes by hand | PR 40 | **Before the projection leaves the process**, which is when it first becomes possible for it to diverge |
| Permitted scopes cost one policy call per candidate scope per request (ADR-022 decision 5). Bounded by the caller's breadth rather than by the corpus, and still the first thing to replace under load - partial evaluation of the policy into a residual query is the named production path | ADR-022 | When search is measured, or when a caller with wide scope appears |
| Search parameters `product` and `effective date` are not what CAP-SCH-001 ultimately means: product binds to what the content names as its subject until master data (capability 5) exists, and effective date waits for effective dating | PR 40 | With capability 5, and with effective dating |

### 2.3 The boundary iteration 1 drew, and why it matters here

Version semantics (capability 2) and lifecycle state (capability 7) were kept separate.
Iteration 1 built immutable version lineage with **no states at all**, on the argument that an
approval gate without segregation of duties and signature capture is worse than none because it
looks like a control and is not one.

Iteration 2 is where that debt is paid: states, workflow, and electronic signature arrive
**together**, or not at all.

## 3. The principle for this iteration

Iteration 1 bought optionality on decisions that were expensive to retrofit. Iteration 2 has a
different job: **make the governance real, and make it demonstrable.**

Every capability here is one a regulated organisation will ask pointed questions about -
who approved this, on what date, against what, and can you prove the person who wrote it did
not also approve it. The measure of success is not that the features exist but that each
question has an answer the platform can produce on demand.

## 4. Scope

### 4.1 In scope

| Capability | What this iteration builds |
|---|---|
| **7 Lifecycle & version management** | A configurable state model (draft, in-review, approved, superseded, withdrawn) with explicitly permitted transitions; effective dating; supersession; per-market regulatory-approval state held separately from internal state (ADR-005) |
| **16 Workflow & approvals** | Review and approval routing, sequential to start; approval invokes signature; segregation of duties enforced |
| **19 e-Signature** | The signature half of capability 19: a manifest binding signer identity, meaning, timestamp, and a hash of the signed version |
| **3 Templates** | A minimal template library: section structure, mandatory flags, and instantiation of a conformant draft. Enough for authoring to be template-driven rather than hand-built FHIR |
| **2 Section identity** | Stable section identifiers assigned at creation, surviving versions and translations (ADR-015 decision 6) |
| **15 Search & retrieval** | Permission-scoped search by product, market, language, status, and identifier; retrieval of a specific version and of the current-approved |
| **5, 6 seams** | Master-data reference fields and terminology binding points, without the upstream integrations |

### 4.2 Out of scope

Localisation and translation (9); change and impact analysis (8); regulatory mapping and
per-market profiles beyond the core pin (10); compliance and completeness checking (12);
rendering (13); publishing (14); migration (4); reusable content units, unless Section 8's
first decision says otherwise.

### 4.3 The deliberate deviation, again

**Capability 15 (search) is pulled to the front of P1** rather than treated as a tail. Once
lifecycle states exist there is something worth searching for - "which labels are awaiting
approval in my market" is the first question anyone asks of a system like this - and CAP-SCH-004
requires results to be permission-scoped, which is cheap now and expensive once a search index
exists that was built without it.

## 5. The slice

```
Author instantiates a draft from a template                      (3)
  -> section identifiers assigned at creation                    (2, ADR-015)
  -> draft, in-review, approved: only permitted transitions      (7)
  -> approval routed to someone who is not the author            (16, 17)
  -> approval captures an electronic signature over a hash       (19)
  -> the approved version is pinned and effective-dated          (7)
  -> a second market approves the same content independently     (7, ADR-005)
  -> search returns it, scoped to the caller                     (15)
```

Every step is audited, every state change emits an event, and everything remains within the
affiliate and market scope enforced in iteration 1.

## 6. Acceptance criteria

| # | Criterion | Requirement |
|---|---|---|
| 1 | An unpermitted transition is rejected; a permitted one records actor and timestamp | CAP-LCM-001, CAP-LCM-007 |
| 2 | The author of a version cannot approve it, through any route | CAP-IAM-006, CAP-WFL acceptance |
| 3 | Approval captures a signature binding signer, meaning, timestamp, and a hash of the exact version signed | CAP-AUD-003 |
| 4 | A version approved in one market is not approved in another, on the same content | CAP-LCM-003, ADR-005 |
| 5 | A label instantiated from a template passes structural validation, and records the template version it came from | CAP-TPL-004, CAP-TPL-007 |
| 6 | Section identifiers survive a new version unchanged | CAP-SCM-007 |
| 7 | Search returns only what the caller may see, and can return the current-approved version per market | CAP-SCH-002, CAP-SCH-004 |
| 8 | Any historical version is reconstructable with the metadata that made it valid | CAP-LCM-006 |

Criterion 2 is the one to build first and hardest. Segregation of duties is the control most
often claimed and least often enforced end to end, and it must hold through every route into a
transition, not only the one the happy path uses.

## 7. Decisions this iteration forces

Each becomes an ADR:

1. **Lifecycle state model representation.** The states and permitted transitions are
   config-as-data (capability 21), but their storage, and how a transition is recorded against
   an immutable version, is not settled. This interacts with ADR-005's separation of internal
   and per-market state.
2. **Electronic signature mechanism - settled by
   [ADR-020](adrs/0020-electronic-signature.md).** The demonstration
   uses **user identifier and password re-authentication at the signing gate**, with the
   signature manifest persisted in the audit trail and linked to the electronic record. PKI is
   the expected production mechanism.

   This is not a demonstration shortcut. 21 CFR Part 11 Subpart C admits non-biometric
   electronic signatures built from **at least two distinct identification components**, of
   which a user identifier and a password is the canonical pair, and Section 11.70 requires a
   signature to be **linked to its record** so it cannot be excised, copied, or transferred.
   What the ADR had to specify, and now does:

   - **Re-authentication at the point of signing**, rather than relying on the session. A
     signature any authenticated session can produce is an authorisation, not a signature.
   - **What is signed**: the hash of the canonical serialisation of the pinned version, so the
     signature covers exactly the content approved and later alteration is detectable.
   - **The manifest**: signer identity, printed name, date and time, and the **meaning** of the
     signature (authorship, approval, review), which Section 11.50 requires to be recorded.
   - **Where it lives**: the append-only audit store from iteration 1, which already refuses
     update and delete at the database, linked to the version signed.
   - **What is met by process rather than mechanism**: password policy, identity proofing,
     revocation, and the administrative controls Part 11 requires of the organisation. Naming
     these honestly matters more than the feature, because a regulated buyer will probe exactly
     this boundary and overclaiming would cost more than it gains.

   Note that **authentication needs no change**: Keycloak already signs users in with a
   username and password, and OIDC is only how the resulting token reaches the API.
3. **Template representation.** D2.1 open item 1: `Questionnaire` plus profile, or a template
   `Composition` skeleton. Deferred through iteration 1 because nothing needed it.
4. **Secondary identifiers.** Where a submitter's or legacy identifier lives, given
   `Bundle.identifier` is 0..1 and ADR-015 gives ours the slot.
5. **How search is scoped - settled by
   [ADR-022](adrs/0022-permission-scoped-search.md).** Scope is a query
   predicate rather than a filter applied to results, the permitted scopes come from the same
   policy that decides a single read, and an unscoped search is not expressible. Section 4.3
   argued search was cheap now and expensive later; this is the decision that was cheap now.

## 8. Open questions for the programme

Answers change the shape of the iteration, so they are worth settling before it starts rather
than during.

1. **Is content reuse in scope?** Reusable units (CAP-SCM-004) with pinned resolution (ADR-007)
   are a significant piece of modelling. Including them makes the content model materially more
   complete and the iteration materially longer.
2. **How much authoring UI? - answered.** A thin, template-driven authoring surface is in
   scope, built on **TipTap** for narrative editing. See Section 8.1.
3. **Is a SNOMED CT licence being pursued?** Terminology (capability 6) cannot be demonstrated
   properly without one, and the lead time is not ours to control.

Backbone first, then the surface: a demonstration that shows an approval with a signature is
more persuasive than a richer editor with no governance behind it, because the governance is
the hard part and the part a regulated buyer cannot get elsewhere.

## 8.1 The authoring surface

**TipTap for narrative editing**, with two constraints that are not optional.

**FHIR narrative is not arbitrary HTML.** `Narrative.div` is a constrained XHTML subset: a valid
XHTML fragment carrying the XHTML namespace, excluding scripts, forms, active content, and
external references. An editor emitting ordinary HTML produces content that fails at the write
gate or, worse, round-trips imperfectly - and CAP-SCM-010 round-trip fidelity is already
enforced by test, so it surfaces immediately rather than silently. The editor schema must
therefore be **restricted to the permitted subset at the point of editing**, not sanitised
afterwards: an author should be unable to produce content the platform will refuse, rather than
discovering it at approval.

**Narrative is not the whole model.** Sections carry coded, structured content as well as
narrative (CAP-SCM-003), and the template (capability 3) says which is which. TipTap edits the
narrative *within* a structure the template defines; it does not define the structure. Letting a
rich-text editor own the document shape is how a structured content platform quietly becomes a
word processor with a FHIR export.

**Licence**: TipTap's core and standard extensions are MIT, satisfying the Apache-2.0 rule in
`CLAUDE.md`. Some extensions, collaboration among them, are commercially licensed. The ADR
should record that the platform stays on the MIT core, so a later convenience does not introduce
a licence obligation unnoticed.

## 9. Delivery sequence

Provisional, and dependent on Section 8. Each row is a pull request, test-first, reviewed.

| # | Pull request | Size |
|---|---|---|
| 1 | Seed the traceability registries for iteration 2; ADR: lifecycle state model | S |
| 2 | Section identity assigned at creation and preserved across versions | M |
| 3 | Lifecycle state machine, config-as-data, with permitted transitions | L |
| 4 | Per-market regulatory-approval state, separate from internal state | M |
| 5 | ADR and mechanism: electronic signature over a pinned version hash | M |
| 6 | Workflow routing, with segregation of duties enforced at every route | L |
| 7 | Template representation ADR, and instantiation of a conformant draft | L |
| 8 | Permission-scoped search and current-approved retrieval | M |
| 9 | Effective dating and supersession | M |
| 10 | Thin authoring surface, if Section 8 question 2 is answered that way | L |

## 10. Exit criteria

Iteration 2 is complete when all eight acceptance criteria in Section 6 pass in CI, the ADRs in
Section 7 are merged, the traceability matrices record evidence for every requirement scheduled
against iteration 2, and the debts in Section 2.2 marked "Iteration 2" are either paid or
re-recorded with a reason.

*End of Iteration 2 plan v0.1.*
