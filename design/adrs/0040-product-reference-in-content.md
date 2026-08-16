# ADR-040: How a label says which product it is about

Status: accepted
Date: 2026-08-16

Realises CAP-SCM-011 and CAP-MDM-003, and pays the debt
[ADR-036](0036-master-data-and-terminology-binding.md) recorded against itself: the product
directory can answer and nothing on the write path asks it.

## Context

A label is about a product, and what the platform holds is a string somebody typed.
`Composition.subject[0].display` is what search indexes, and it is free text: it cannot be
resolved, cannot be compared across labels, and cannot answer *"which labels are about this
product"* - the question every change-impact and portfolio view starts from.

ADR-036 built the port. `IProductDirectory` resolves an identifier to a product, over whatever
the organisation's system of record is. It has never been asked anything, because nothing in
content refers to a product by identifier.

The question this settles is what a label carries when it does.

## Decision

**1. A label refers to a product by identifier, not by resource.** `Composition.subject` carries
a reference with an `identifier` - the product's identity in the system of record - and a
`display` for a reader. It does not point at a `MedicinalProductDefinition` this platform hosts.

FHIR permits a reference by identifier precisely for this: the referent lives in another system.
And here it genuinely does. CAP-MDM-002 makes SPOR or an internal MDM platform the system of
record for product data, and ADR-036 decision 1 makes it something behind a port. A platform
that minted its own `MedicinalProductDefinition` resources would be asserting a second system of
record for data it does not own, which is the same mistake as storing the authoring projection.

**2. The display is carried and is never what anything resolves.** A human reading a leaflet
needs a name; nothing else may use it. It is a copy of what the directory said when the
reference was written, and copies go stale - so it is written for readers and every question the
platform asks goes to the identifier.

**3. The identifier is indexed, and search gains a facet for it.** This is the whole point of
doing it: *"which labels are about this product"* becomes a query rather than a scan of every
label's free text.

**4. Nothing verifies the product exists at the write gate.** The directory may be unreachable,
and ADR-036 decision 4 says resolution never blocks a write on an external system being
available. A reference to a product the directory does not know is stored, and the surface says
so when it cannot resolve one.

This is deliberately the weaker of the two options and it is the one consistent with everything
else: a write gate that failed because a master-data system was restarting would make an
external system's availability a precondition of authoring. Reference integrity for master data
is a reconciliation question (CAP-MDM-004), answered the way inert registrations are - by looking
afterwards - rather than a gate question.

**5. A label may say nothing about a product.** Content arrives from elsewhere, and a template
instantiated before anybody chose a product is a normal state of affairs. An absent subject is
absent, not empty, and not an error.

## Alternatives considered

**Keep the display and add the identifier beside it.** What this is, and it took a paragraph to
notice it was not a compromise: the display was already there, so the change is that something
resolvable joins it and the display stops being what anything reads.

**Host `MedicinalProductDefinition` and reference it literally.** Rejected under decision 1. It
would also mean synchronising product data into this platform, which is CAP-MDM-005's governed
replica - a real capability, deliberately not built, and not a prerequisite for saying which
product a label is about.

**Put the product identifier in a tag, as scope and template are.** Tags carry things the
platform itself asserts about a document (ADR-017). A product reference is content: it is part
of what the label says, it appears in a rendered leaflet, and a regulator reads it. It belongs
in the resource.

## Consequences

`config/identifiers.json` gains a product system, and an adopting organisation that leaves it
unset gets the same conspicuous documentation domain as every other identifier there.

Content written before this exists has a display and no identifier. It is readable, it is not
resolvable, and nothing rewrites it - which means "which labels are about this product" answers
only for labels written since. Stated rather than papered over, because a facet that silently
omits older content is worse than one that does not exist.

Nothing yet lets an author choose a product: this is the platform half. The surface picks one
the same way it picks a label, and that is the following change.
