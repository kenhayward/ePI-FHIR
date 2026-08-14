# ADR-021: Template representation

Status: accepted
Date: 2026-08-14

Realises CAP-TPL-001 to CAP-TPL-004 and CAP-TPL-007. Closes D2.1 open item 1, which asked
for the FHIR representation of a template to be "decided in D3". Required by iteration 2
([iteration-2.md](../iteration-2.md) Section 7 decision 3, acceptance criterion 5).

## Context

D2.1 offered two candidates: a `Questionnaire` plus profile linkage, or a template
`Composition` skeleton. Choosing between them turns on what a template is actually for.

A template does two jobs. It says **which sections this kind of label has** - their order,
which are mandatory for this organisation, what boilerplate they start with - and it drives
**guided authoring**, shielding an author from raw FHIR (CAP-TPL-005).

Neither job is conformance. The structure a label must satisfy to be valid is already defined
by the pinned implementation guide (ADR-016), and a template does not get to redefine it. What
a template adds is an organisation's choices *within* that structure, which is a different kind
of statement and one no published profile makes.

Two further requirements constrain the answer, and they pull in opposite directions.
CAP-TPL-008 gives a template its own lifecycle - draft, in-review, approved, retired, approved
through the workflow capability - and CAP-TPL-007 requires templates to be versioned with
effective dates. Those are the properties of *content*, not of configuration: a template is
authored, reviewed and approved by people, not set by an administrator in a file.

## Decision

**1. A template is a platform-native definition, not a FHIR resource.** It is a versioned
structured document naming the label type it serves, the profile it targets, and its section
tree: identifier, code, title, cardinality, ordering, and optional boilerplate.

**2. It targets a profile rather than restating one.** The pinned implementation guide says
what a conformant ePI is; the template says which of its sections this label type uses and in
what order. A template that could contradict the profile would be a second, competing
definition of conformance, and the one that lost would do so at the write gate.

**3. Instantiation produces a document Bundle anchored by a `Composition`,** scaffolded with
the template's sections in order, and it is validated at the write gate like any other content.
A template that cannot produce a conformant draft is a broken template, and the existing gate
is what says so - there is no second validation path.

**4. Every version records the template and template version it came from** (CAP-TPL-007), on
the content itself, so the provenance survives independently of any registry the platform keeps.

**5. Templates are content, so they get content's machinery.** Versioned, immutable per version,
lifecycle-managed and approvable through the same engine labels use (ADR-019). Not
configuration under `config/`: an administrator editing a file is the wrong control for
something a regulatory author owns and an approver signs off.

**6. Guided authoring may later be projected to a `Questionnaire`,** generated from the
template rather than authored as one. If a form-rendering client is ever wanted, that is the
adapter to write, and it changes nothing about where the truth lives.

## Alternatives considered

- **`Questionnaire` plus profile linkage.** The FHIR-native answer for guided data capture, and
  genuinely good at prompts, `enableWhen`, and answer value sets. Rejected as the source of
  truth: an ePI section is mostly narrative, and modelling narrative-rich labelling as
  questionnaire items fits badly - the authoring surface this platform is heading towards is a
  rich-text editor within a fixed structure (iteration-2 Section 8.1), not a form renderer. A
  Questionnaire also says nothing about the document structure that must come out the other
  end, so a `StructureMap` or bespoke mapping would be needed anyway. Decision 6 keeps it
  available as a projection.
- **A template `Composition` skeleton.** Attractive because instantiation becomes a copy.
  Rejected: a `Composition` is an instance, so a library of them as definitions conflates the
  two, and there is nowhere to put mandatory flags, ordering rules or authoring guidance
  without extensions that would then be ours to define and maintain anyway. A skeleton also
  cannot express "this section is optional" - an absent section and an optional one look
  identical.
- **A generated `StructureDefinition` per template.** Formally the most correct: constraining
  sections is exactly what a profile does. Rejected on cost and on audience - generating and
  publishing profiles per label type per market is a large machine, and the resulting artefact
  says nothing about boilerplate or guidance, which is half of what a template is for.
- **Templates as configuration under `config/`.** Consistent with ADR-012, and wrong here.
  CAP-TPL-008 requires a template lifecycle with approval, and an administrator editing a file
  is the wrong control for something a regulatory author owns and an approver signs.

## Consequences

- Iteration 2 implements the template definition, instantiation, and the recording of template
  provenance on instantiated content. The template library's own lifecycle reuses the state
  machinery already built, and is a later increment.
- Because a template targets a profile rather than restating one, an implementation-guide
  upgrade (ADR-016) can invalidate a template. That is the correct failure: it surfaces at the
  write gate when a draft is instantiated, rather than at approval when it is expensive.
- Variant templates by inheritance (CAP-TPL-006) and boilerplate as reusable units
  (CAP-TPL-011) both remain open. Neither is precluded by this decision, and reusable units in
  particular depend on ADR-007's pinning rules.
- A form-rendering client is not available for free. If one is wanted, decision 6 is the route,
  and the work is a projection rather than a change of representation.
