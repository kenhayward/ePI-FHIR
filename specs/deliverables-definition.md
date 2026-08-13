# FHIR ePI Enterprise System - Deliverables Definition

**Status:** Draft for agreement, **Date:** 2026-08-13
**Scope baseline (agreed):** HL7 FHIR ePI IG (Gravitate Health / Vulcan), EU EMA ePI Common Standard + QRD/PQ/CMDh, ISO IDMP & SPOR master data, US FDA SPL + additional national schemes (MHRA, Swissmedic, Health Canada, etc.)
**Posture:** Greenfield build (new system; integrate rather than replace at the edges); concrete technology choices deferred to D3, **Audience:** Internal engineering (deep technical depth)

---

## 1. Purpose of this document

Defines the three deliverables that make up the specification set, what each contains, where the boundaries sit between them, and the order in which they should be produced. This is the contract for the work - once agreed, each document is written against the structure below.

## 2. The three deliverables at a glance

| # | Document | Answers | Primary reader | Depth |
|---|----------|---------|----------------|-------|
| D1 | **Solution Overview Specification (SOS)** | *What* the system is and *why* - scope, context, capability map, guiding architecture and principles | Delivery leads + engineers orienting to the system | Breadth over depth |
| D2 | **Detailed Capability Specification (DCS)** | *What each capability must do* - functional behaviour, content/data model, standards mapping, lifecycle, rules, acceptance criteria | Engineers building each capability | Deep, per capability |
| D3 | **Detailed Technical Architecture (DTA)** | *How it is built* - components, stack, data stores, APIs, integration, security, deployment, NFRs | Engineers implementing and operating | Deep, prescriptive |

The three are layered: **D1 frames**, **D2 specifies behaviour**, **D3 specifies construction**. Requirements traceability runs D2 -> D3 (every capability requirement lands on one or more architectural components).

---

## 3. D1 - Solution Overview Specification (SOS)

**Goal:** a single anchoring document any engineer reads first to understand the system end to end.

1. Introduction - problem statement, business drivers, objectives, glossary
2. Scope - in/out of scope, target regions and regulators, phasing
3. Domain primer - ePI concept, FHIR ePI vs PDF/HTML representations, IDMP/SPOR, key regulatory frameworks and how they differ
4. Stakeholders & actors - regulators, local affiliates, authoring, QA/regulatory ops, consumers/APIs
5. Capability map - the full capability catalogue (feeds D2), one-line each, grouped into domains
6. Solution context - system context diagram, upstream/downstream systems, boundaries
7. Guiding architecture - high-level logical architecture, key patterns, build posture rationale
8. Architecture principles & standards baseline - the standards stack and conformance targets
9. Non-functional summary - headline NFRs (the detail lives in D3)
10. Key risks, assumptions, dependencies, open decisions
11. Delivery roadmap - phasing and sequencing at a capability level

## 4. D2 - Detailed Capability Specification (DCS)

**Goal:** the functional source of truth. Organised as one section per capability, each written to a fixed template so they are comparable and traceable.

**Per-capability template**
- Purpose & scope
- Actors and primary/secondary flows
- Functional requirements (uniquely IDed, e.g. `CAP-ING-012`)
- Content & data model (FHIR resources/profiles, extensions, referentials)
- Standards & regulatory mapping (FHIR ePI IG, EMA ePI, SPL, national variants)
- Lifecycle & state model where the capability owns state
- Business rules & validation
- Interfaces consumed/exposed (logical - realised in D3)
- Acceptance criteria / conformance
- Dependencies on other capabilities

**Capability catalogue** - *target state*, grouped. This is the full target capability set; delivery is phased. D1's roadmap sequences it and D2 records, per capability, a target-state definition plus the delivery phase. Planning the whole target now is deliberate: it surfaces the data-model, extensibility and integration choices that are expensive to retrofit (see Section 6).

*Content & authoring*
1. Ingestion & Import (FHIR Bundle, PDF, HTML; conversion/authoring on-ramp)
2. Structured Content Model (FHIR ePI Bundle/Composition, document structure, sections; content reuse / single-sourcing of shared blocks and class labelling, cross-references)
3. Template & Label-Type Management (managed library of label-type templates - EU SmPC/PL, US SPL, QRD and product-type variants; pre-scaffolded FHIR ePI skeletons with mandatory sections, section metadata and terminology bindings; guided/template-driven authoring to shield authors from raw FHIR; template versioning and derivation of market variants from a core template)
4. Data Migration & Legacy Onboarding (one-time and ongoing bulk onboarding of legacy labels - PDF/SPL/Word - into the structured model; mapping, back-conversion, reconciliation and load; shapes the ingestion/conversion architecture, so it is planned now rather than bolted on)

*Reference & master data*
5. Master Data & Identifiers (IDMP/SPOR linkage - substance, product, organisation, referentials; the product <-> packaging <-> label association model)
6. Terminology & Code System Management (managed code systems and value sets - SNOMED CT, EDQM standard terms, UCUM, MedDRA, ISO language/country, EMA/FDA controlled vocabularies; terminology server, value-set binding and version management that validation, templates and authoring all depend on)

*Lifecycle, change & localisation*
7. Lifecycle & Version Management (state model, versioning, effective dating, supersession/withdrawal; internal lifecycle state vs per-market regulatory-approval state)
8. Change Management & Impact Analysis (intake of source changes - CCDS/CDS updates and clinical safety signals; change/diff tracking across versions; downstream impact analysis across products, regions and affiliates; linkage to regulatory submissions/variations and to the triggering signal)
9. Localisation, Multi-region & Translation Management (country, language, regulator variants; translation workflow, translation-memory/TMS integration and linguistic review - material given the EU's 24+ languages)
10. Regulatory Mapping & Conformance Profiles (per-scheme mapping, conformance profiles, national extensions - defines the target structure each market expects)

*Quality & production*
11. Validation & Quality (technical validation - FHIR conformance, terminology binding, structural well-formedness)
12. Compliance & Completeness Checking (regulatory completeness and governance - completeness against the market template, missing mandatory sections, section-level structural issues, business/compliance rule checks, and CDS-origin traceability flagging label sections with no approved source origin)
13. Rendering & Transformation (FHIR -> HTML -> PDF, QRD/templates, styling, accessibility)
14. Publishing & Distribution (channels, portals, national databases, syndication APIs; scheduled/effective-dated publication and embargo handling)

*Access & governance*
15. Search, Access & Retrieval (FHIR API, query, consumer access)
16. Workflow & Approvals (review, e-signature, audit trail)
17. Identity, Access Control & Permissions - RBAC/ABAC (authentication, SSO/federation; combined role- and attribute/scope-based authorisation; permission scoping across affiliate/organisation, region/market, product & label scope, lifecycle state, and template; functional roles - author, reviewer, approver, publisher, template-owner, admin; permission granting with delegated, affiliate-scoped administration; segregation of duties; multi-tenant isolation of affiliate data)
18. Security (encryption at rest/in transit, secrets management, threat and vulnerability controls, network and application security)
19. Audit Trail, e-Signature & Inspection Support (comprehensive tamper-evident audit trail of all GxP-relevant actions - who, what, when, why, before/after values; electronic signatures per 21 CFR Part 11 & EU Annex 11 - signing events, signature meaning/manifest, binding to the signed record; full reconstruction of a record's history; auditor/investigator access via a read-only inspection role and audit mode; audit-trail search, filtering and visibility for periodic audit-trail review and regulatory inspections; export and reporting for inspection support)

*Platform & operations*
20. Notifications, Events & Subscriptions (event backbone; FHIR Subscription; alerting affiliates and consumers when a change or new version affects them - the spine that change management and integration ride on)
21. Configuration & Business-Rule Management (externalised, config-as-data management of validation/compliance rules, lifecycle states, template and terminology bindings, and market/regulator definitions - so a new country, rule or scheme is a configuration change, not a code release)
22. Records Retention & Archival (GxP retention schedules, archival, legal hold and defensible disposition of labels and their audit records)
23. Reporting & Analytics (operational + regulatory reporting)
24. External Integration (RIM/regulatory submission, DMS, identity, notification)

**Capability boundary notes (the four related quality/compliance capabilities)**
- **#10 Regulatory Mapping & Conformance Profiles** - defines *what the target should look like* per scheme (structure, conformance profiles, national extensions). It is the source of truth the checks run against.
- **#3 Template & Label-Type Management** - turns those profiles into *authorable skeletons*; the market template it produces is the yardstick for completeness.
- **#11 Validation & Quality** - *is the artifact technically valid FHIR* (conformance, terminology, well-formedness).
- **#12 Compliance & Completeness Checking** - *does this label actually satisfy the market template and governance rules* (missing/extra sections, structural issues, compliance rules, and every section traceable to an approved CDS/CCDS origin). Consumes the template from #3, the profiles from #10, and the change lineage from #8.

**Access-scoping note (#17)** - the RBAC/ABAC permission model is cross-cutting: its scopes govern who may act on templates (#3), master data (#5), and label variants by affiliate/region (#9), and it enforces the segregation of duties that Workflow & Approvals (#16) relies on. #17 is *who may do what, where*; #18 Security is *protection*; #19 Audit is *the evidentiary record*.

**Audit & e-signature note (#19)** - the e-signature *event* is triggered at approval gates in Workflow & Approvals (#16); the e-signature *mechanism and its Part 11/Annex 11 record* are owned by #19, which also captures the audit trail every other capability writes to. #19 is the read/inspection side (search, visibility, reconstruction, auditor access); #16 is where signing is invoked in-flow.

## 5. D3 - Detailed Technical Architecture (DTA)

**Goal:** a prescriptive greenfield architecture an engineering team can build from.

1. Architecture overview & drivers - recap of constraints, principles, decision method (ADRs)
2. Logical architecture - components/services, responsibilities, dependency map
3. Data architecture - FHIR store choice, canonical vs rendered content, terminology/referential stores, master-data model, storage of PDF/HTML binaries, data retention
4. Content processing architecture - ingestion pipeline, transformation/rendering pipeline, validation pipeline
5. API & interface design - FHIR RESTful API surface, internal service APIs, event/streaming model, contracts
6. Integration architecture - patterns and adapters for RIM, submission, DMS, SPOR/IDMP sources, identity
7. Lifecycle & workflow implementation - how state, versioning, approvals are realised technically
8. Security architecture - identity, RBAC/ABAC, multi-tenancy for affiliates, encryption, GxP/21 CFR Part 11 & Annex 11, audit
9. Cross-cutting concerns - observability, config, error handling, i18n, feature flags
10. Deployment & infrastructure - target cloud, containers/orchestration, environments, IaC, CI/CD
11. Non-functional architecture - scalability, availability/DR, performance budgets, capacity
12. Technology stack - concrete selections with rationale and alternatives considered
13. Key runtime scenarios - sequence diagrams for critical flows (ingest->validate->publish, variant creation, update/supersede)
14. Architecture decision records - significant decisions captured

---

## 6. Target-state architecture decisions to settle now (so they don't bite later)

These are the choices that are cheap to make now and expensive to retrofit once data, integrations and content exist. They are resolved as principles in D1, locked as architecture in D3, and several shape capability behaviour in D2. Captured here so target planning fixes them deliberately rather than by accident of first implementation.

*Foundational (fix before D3)*
- **Canonical information model & content reuse** - one canonical FHIR representation, with an explicit strategy for single-sourcing shared content (class labelling, reusable blocks) and cross-references, so reuse is modelled rather than faked by copy-paste. (Drives #2, #3, #8, #9.)
- **Event-driven backbone** - commit early to an eventing model; change propagation (#8), notifications (#20), near-real-time impact analysis and integration (#24) all ride on it. Retrofitting events onto a request/response core is costly.
- **Extensibility / config-as-data** - new markets, regulators, terminologies and rules must be addable without a code release (#21). Decide the configuration model up front or every new country becomes a project.
- **Multi-tenancy & data partitioning for affiliates** - isolation vs shared, and how permission scopes (#17) map to physical partitioning. Foundational and hard to change once data lands.
- **Identifier & versioning scheme** - global, stable identifiers and version semantics for labels/documents/sections across languages and markets; underpins traceability, cross-references and supersession.
- **Regulatory-approval state vs internal lifecycle** - model the per-market "currently approved" version distinctly from internal workflow state (#7). Conflating them corrupts publishing and impact analysis.

*Platform / delivery*
- **CSV / GAMP 5 validation strategy** - the system must be *validatable*: environment control, testability, traceability, audit (#19), controlled release. Build into the architecture and CI/CD, not after go-live.
- **Public API & interoperability strategy** - FHIR API surface, API versioning policy, gateway/management, external-consumer onboarding (#15, #24). Locking versioning policy early avoids breaking downstream consumers.
- **Effective-dating, scheduling & embargo** - publish-on-approval-date and embargo semantics (#14) modelled once, centrally.

*Scope boundaries*
- **Packaging / labelling artwork - RESOLVED: out of scope for authoring.** Print and packaging artwork (leaflet and carton DTP, graphics, pictograms, Braille layout) is produced by external agencies in Adobe Illustrator and delivered as print-ready PDF. This system is the source of truth for the *structured ePI content* and renders the electronic FHIR/HTML/PDF representations (#13); it does **not** author artwork. Agency-delivered Illustrator/PDF artwork is ingested (#1) and held as a managed asset linked to the approved label version, for reference and for artwork-vs-approved-text reconciliation. No live integration to a DTP/artwork-management system is assumed beyond that asset hand-off.
- **Translation management - RESOLVED: in-system.** Full translation management - translation workflow, translation memory and linguistic review - is delivered within the system (#9); no external TMS is assumed. An external-TMS integration remains an optional future path via #24.
- **Patient/HCP-facing delivery & ePI "focusing" - RESOLVED: option (A).** This system is the authoritative back-office source of truth that publishes approved ePI and its representations to downstream channels (national regulator databases, a company ePI repository/API); patients and HCPs consume via *external* channels. Consumer-facing delivery and *ePI focusing* (option B - Gravitate Health-style personalisation) are out of scope for this programme: a separate, later solution, most likely a third-party product, that this system would feed. Consequently the publishing, retrieval and integration capabilities (#14, #15, #24) must expose approved content and its representations cleanly enough for such a downstream consumer system to build on.

## 7. Boundaries between documents (to avoid overlap)

- **Capability catalogue** is introduced in D1 (one-liners) and fully specified in D2. D1 does not define requirements.
- **NFRs** are summarised in D1 and architected in D3. D2 references them where a capability constrains one.
- **Data/content model** appears in D2 at the logical/FHIR-profile level; D3 covers physical storage and stores.
- **Interfaces** are logical in D2 (what a capability exposes/consumes); technical contracts and protocols are in D3.
- **Architecture decisions** in Section 6 are stated as principles in D1 and resolved with rationale in D3 (as ADRs).

## 8. Cross-cutting artefacts (shared across all three)

- Glossary & acronyms, Standards register (with versions/conformance targets), Requirements traceability matrix (D2 IDs -> D3 components), Assumptions/decisions log (ADRs), Target-state vs phased-delivery view of the capability catalogue.

## 9. Format & production

- Authored in Markdown; delivered as `.docx` on request for review/sign-off.
- Suggested sequence: **D1 -> D2 -> D3.** D1 fixes scope and the capability catalogue; D2 depends on that catalogue; D3 depends on D2's requirements and interfaces.
- Each document can be produced in full, or section-by-section for review - recommended for D2/D3 given their size.

## 10. Open decisions before drafting D1

1. Confirm/adjust the **capability catalogue** in Section 4 (add, merge, drop).
2. **Scope boundaries - all resolved** (Section 6): artwork out of scope for authoring; translation in-system; patient/HCP-facing delivery is option (A). No open scope items.
3. **Technology and architecture selection is deferred to D3** (cloud platform, application stack, FHIR server, component products). D3 evaluates and recommends with rationale; D1 commits only to architectural style/principles, not products.
4. Confirm any **mandated FHIR server / existing components** that must be reused - an input constraint feeding D3's selection. Use the checklist in Section 11; to confirm before D3.
5. Confirm **compliance regime** to design to (GxP / 21 CFR Part 11 / EU Annex 11) - assumed in scope.

## 11. Mandated vs open components (input constraints for D3)

Greenfield does not mean every component is a free choice. Enterprises usually already own platforms a new system is expected to build on (for cost, support, security-approval or governance reasons). Each component below is either **mandated** (a fixed point D3 must design around) or **open** (D3 evaluates and recommends). This is an *input constraint*, not a design choice - confirm it before D3 so D3 evaluates only what is genuinely on the table. Default where unset: **open**.

| Component | What it is / why it matters | Example products | Mandated or open | Notes / named product |
|---|---|---|---|---|
| **FHIR server** | Stores/serves FHIR resources (ePI Bundles), FHIR REST API, validation, search - the heart of the system. Choice drives language/hosting, extensibility, terminology support, performance, licensing. | HAPI FHIR (Java), Firely Server (.NET), Azure Health Data Services FHIR, Google Cloud Healthcare API, AWS HealthLake, Smile CDR | _tbc_ | |
| **Identity provider (IdP/SSO)** | Authentication and federation for capability #17 (RBAC/ABAC). | Entra ID (Azure AD), Okta, Ping, Keycloak | _tbc_ | |
| **Terminology server** | Manages code systems/value sets for #6 (SNOMED, EDQM, MedDRA, UCUM, LOINC); may already be licensed. | Ontoserver, HAPI terminology, Snowstorm, FHIR terminology service | _tbc_ | |
| **Cloud platform / tenancy** | Hosting, managed services, region/data-residency posture. | Azure, AWS, GCP, on-prem/private | _tbc_ | |
| **Application stack / runtime** | Primary implementation language(s) and framework(s) for services and UI. | .NET, Java/Spring, Node/TypeScript, Python | _tbc_ | |
| **Event/messaging backbone** | The event spine for #20 (change propagation, notifications, integration). | Kafka, Azure Service Bus/Event Hubs, RabbitMQ, AWS SNS/SQS | _tbc_ | |
| **Document management system (DMS)** | Existing controlled-document store to integrate with (#24). | Veeva Vault, OpenText, SharePoint | _tbc_ | |
| **RIM / regulatory submission** | Regulatory information management and submission dispatch to integrate with (#24, change #8). | Veeva Vault RIM, ArisGlobal, in-house RIM | _tbc_ | |
| **Master data source (IDMP/SPOR)** | System of record for product/substance/org identifiers referenced by #5. | EMA SPOR, internal IDMP/MDM platform | _tbc_ | |
| **CI/CD & IaC tooling** | Build/release and infrastructure automation; relevant to GxP/CSV controlled release. | Azure DevOps, GitHub Actions, GitLab, Terraform | _tbc_ | |
| **Observability stack** | Logging/metrics/tracing for operations and audit support. | Azure Monitor, Datadog, Elastic, Grafana/Prometheus | _tbc_ | |
| **Rendering/PDF toolchain** | Engine(s) for FHIR -> HTML -> PDF rendering (#13). | Antenna House, PrinceXML, Apache FOP, headless browser | _tbc_ | |

Fill the **Mandated or open** column (and name the product where mandated) before D3. Anything left "open" is D3's to recommend.
