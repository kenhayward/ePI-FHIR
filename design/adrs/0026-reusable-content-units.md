# ADR-026: Reusable content units, and how a label refers to one

Status: accepted
Date: 2026-08-14

Realises CAP-SCM-004 and prepares CAP-SCM-005. Required by iteration 3
([iteration-3.md](../iteration-3.md) acceptance criteria 1 and 2, delivery row 2), and settles
the representation question ADR-007 left open: ADR-007 fixed the resolution *policy* - pinned by
default, track-latest opt-in - without saying what a unit is or how a label names one.

## Context

The same warning appears in a hundred labels. Today it would be typed into each of them, and
the first safety change to that warning becomes an exercise in finding every copy. Reuse is the
capability that makes a body of labelling maintainable, and it is the one D1 Section 6 named as
expensive to retrofit.

Two questions have to be answered together, because the answer to each constrains the other:
**what is a unit**, and **what does a label hold when it uses one**.

The second is harder than it looks. A FHIR document `Bundle` is self-contained: everything a
`Composition` references travels inside the bundle. A label that merely points at a unit held
elsewhere is not a conformant FHIR document, and the write gate cannot validate content it does
not have. A label that contains the unit's text has copied it - which is the thing reuse exists
to prevent.

The pinned implementation guide does not decide this for us. It profiles `Bundle`,
`Composition`, and the product graph; it defines no resource for a reusable block of narrative.

## Decision

**1. A reusable unit is content, in the same shape and the same store as a label.** A document
`Bundle` anchored by a `Composition`, with the platform's own identity (ADR-015), immutable
versions, lifecycle state (ADR-019), approval under signature (ADR-020), audit and search. Not
a new resource type, not configuration, and not a table of its own.

The reason is that everything a unit needs already exists and is already tested. A unit is
authored, reviewed and approved by the same people, under the same controls, for the same
reasons - and a class warning that could be edited without an approval would be a hole straight
through the middle of the governance built in iteration 2. This is ADR-021's argument about
templates, reaching the same conclusion from the same premises.

**2. A label refers to a unit by business identifier and version, never by server id.** The
reference names the unit's identity (ADR-015) and the version it is pinned to. A logical id
would not survive a change of FHIR server (ADR-003), and a reference without a version is a
reference to whatever the unit says today.

**3. The reference is the authored form and the source of truth for reuse.** What an author
creates, and what change impact reads, is "this section uses unit X". That is the record of the
relationship, and it is what makes "which labels use this warning" answerable at all.

**4. The stored version materialises the referenced content, and records what it materialised
from.** At the write gate, once, the pinned unit version's narrative is placed into the section,
and the section keeps the reference alongside it. So the stored label is a conformant,
self-contained FHIR document whose bytes are fixed - and it says, on its face, which unit and
which version each borrowed passage came from.

This is the decision worth arguing with, because it looks like the copying the iteration exists
to prevent. The difference is that a materialised passage **cannot drift from the version it
names**: unit versions are immutable, so the text and the reference agree by construction. What
it can become is *stale relative to a newer unit version*, which is a different thing, is
visible by comparison, and is exactly what propagation is for.

**5. Pinned means pinned, and propagation is a new label version.** A later version of a unit
changes nothing about a label already written. Track-latest (ADR-007) does not mutate an
existing version either: it marks the label as having a newer unit available, and propagation
creates a **new label version** through the ordinary write path - validated, registered,
lifecycle-managed, audited. There is no path by which approved content changes underneath
anyone.

**6. Resolution is deterministic and happens once.** The same label version and the same unit
versions produce the same bytes, because the bytes were produced once and stored. Nothing
resolves at read time, so a read cannot fail because a unit is unavailable, and a render cannot
differ from what was approved.

## Alternatives considered

- **Hold only the reference, and resolve on read.** One copy of the text, and the smallest
  stored artefact. Rejected: the stored canonical form would not be a conformant self-contained
  document, so the write gate could not validate it, and every consumer - render, publish,
  submit - would have to resolve first and could get a different answer if it resolved at a
  different moment. It also makes a read of the content store depend on the content store.

- **Inline the text with no reference kept.** Simple, conformant, and it throws away the only
  record of *why* the passage is there. Change impact then becomes text search, which is how the
  problem is solved today in the systems this platform is meant to replace.

- **`ClinicalUseDefinition` for reusable clinical statements.** The IG profiles it for warnings,
  contraindications, indications, interactions and undesirable effects, and for *structured
  clinical facts* it is the FHIR-native answer - referenced from the product graph rather than
  copied. Not so much rejected as a different problem: this ADR is about reusable **narrative**,
  the paragraphs a label shares. Where a reusable statement is genuinely a clinical fact it
  belongs in a `ClinicalUseDefinition` and should not be a narrative unit at all. Worth revisiting
  when structured clinical content arrives.

- **`Library`.** FHIR's container for knowledge assets, whose `content` attachment could hold
  narrative. Rejected: it is for logic and data used by knowledge artefacts, and using it for
  label prose would be borrowing a resource for its shape rather than its meaning - the mistake
  that makes a FHIR implementation unreadable to the next team.

- **A unit as a section inside one shared-content label.** Cheap, needs no new concept, and makes
  every unit share one lifecycle and one approval. Rejected: units are approved individually or
  the approval means nothing.

## Consequences

- Units flow through every gate a label does, which is the point, and they occupy the same
  identity namespace. A unit and a label are distinguishable by a content type tag rather than by
  where they are stored.
- A label version's bytes include borrowed text, so a label is larger than the sum of what its
  author wrote. That is the price of a self-contained conformant document, and it is what makes
  deterministic rendering achievable at all.
- "Which labels use unit X" is answered from the references and needs an index to be efficient.
  The search projection is the natural home; recorded as a debt rather than built here.
- Track-latest needs something to notice a newer unit version and mark labels accordingly. That
  is change impact (capability 8), out of scope for this iteration - so track-latest arrives as a
  recorded intent on the reference, with the propagation trigger following.
- Cross-references (CAP-SCM-005) reuse this reference shape between sections of the same and of
  different documents. Same mechanism, different target.
