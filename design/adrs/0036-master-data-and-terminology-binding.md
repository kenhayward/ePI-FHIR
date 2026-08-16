# ADR-036: Master-data and terminology binding points

Status: accepted
Date: 2026-08-16

Realises CAP-MDM-004, CAP-MDM-008 and CAP-TRM-007 in part, and completes
[ADR-023](0023-historical-version-reconstruction.md), whose pinned validating context records
conformance packages and not the terminology a version was validated against. Required by
iteration 3 ([iteration-3.md](../iteration-3.md) delivery row 12), where capabilities 5 and 6
are the oldest debt in the plan - deferred through three iterations.

## Context

Two things a label depends on are, today, neither modelled nor recorded.

**Master data.** A label is about a product, and `Composition.subject.display` is what the
platform currently indexes as one. That is a string a submitter wrote. It cannot be resolved,
cannot be compared across labels, and cannot answer "which labels are about this product" -
which is the question every change-impact and portfolio view starts from.

**Terminology.** Codes in a label come from external code systems - SNOMED CT, MedDRA, EDQM,
LOINC - each versioned, each licensed separately, each with its own release cadence. Validation
resolves codes today from the pinned conformance packages, which is offline and reproducible and
covers only what those packages happen to contain.

The second has a sharper edge than it looks. ADR-023 exists so that a version can be
reconstructed with what it was approved against, and its pinned context records conformance
packages by name, version and digest. It records nothing about terminology. A code that was valid
at approval because the code system said so, in the version of the code system in force that
month, is a code the platform cannot later say anything about. That is a gap in exactly the
mechanism ADR-023 was written to close.

**What this ADR deliberately does not decide.** Which terminology server, and which source for
which concept domain, is a programme question that is open and stays open - the platform owner
has asked to review it separately. This record is written so that the answer, whatever it is,
is a configuration change and a component behind a port, not a redesign. Everything below is
chosen to survive being wrong about the source.

## Decision

**1. Master data and terminology are reached through ports the platform owns, never through a
vendor's client.** `IProductDirectory` answers what a product reference resolves to;
`ITerminologyDirectory` answers what a coding means and which version of which system said so.
Both are defined in terms of this platform's concepts, not a supplier's API, which is the
anti-corruption layer capability 24 asks for and the thing that makes the source replaceable.

**2. A binding names the system and the version, and the version is never inferred.** A coding
carries a system and a code; what a validation or a resolution asserts is that *this version of
that system* recognised it. Where a source cannot say which version answered, the platform
records that it could not, rather than recording the version it would use today.

That distinction is the whole reason this exists. "SNOMED CT said so" is not a fact an
inspection can check; "the 2026-03-01 international release said so" is.

**3. Terminology bindings join the pinned validating context.** `PinnedContext` gains the
bindings in force at approval, written by the same transaction that writes the pin (ADR-024
decision 2). A version reconstructed years later reports the conformance packages *and* the
terminology versions it was approved against.

An approval with no terminology bindings is recorded as having none, and is distinguishable from
an approval that was never asked. A pin that silently omitted them would look identical to a pin
taken before this existed.

**4. Resolution never blocks a write on an external system being reachable.** A directory that
cannot answer says so, and the caller decides. A write gate that failed because a terminology
server was restarting would make an external system's availability a precondition of authoring,
which is the coupling capability 24 exists to prevent.

**5. The reference implementations are configuration.** A product directory over
`config/master-data/` and a terminology directory over the pinned packages, so the platform runs
end to end with no external dependency at all - the same arrangement every other adopted
component already has. A demonstration needs a small, synthetic, legible product set far more
than it needs a real one.

**6. No licensed terminology content enters this repository.** SNOMED CT, MedDRA, EDQM and LOINC
are content under their own terms, and an open-source terminology server does not make its
loaded content redistributable. The reference directory carries synthetic codes in a synthetic
system, and the shape of a real binding rather than its content.

## Consequences

The pinned context grows a field, which is a schema migration and is applied as one (ADR-024).
Pins written before this exist have no bindings and are read as having none - which is true of
them, and is why decision 3 distinguishes "none" from "not asked".

`Composition.subject.display` remains what search indexes, because nothing yet writes a resolved
product reference into content. The directory can answer; nothing asks it on the write path.
That is the honest state and it is recorded as a debt: the port exists, the binding of content
to it does not, and doing it properly needs the authoring surface, which is where a product is
chosen rather than typed.

Terminology validation still resolves from the pinned packages. This ADR changes what is
*recorded* about that, not where it comes from. When the source question is answered, the change
is a directory implementation and a configuration entry.

The Snowstorm-serves-R4-while-the-platform-is-pinned-to-R5 mismatch recorded against ADR-016 is
untouched and stays open. It is a property of one candidate source, and choosing sources is the
question being held.
