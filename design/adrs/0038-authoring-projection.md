# ADR-038: The authoring projection

Status: accepted
Date: 2026-08-16

Realises CAP-TPL-005 and CAP-SCM-009. Completes [ADR-037](0037-authoring-surface.md), whose
decision 2 says an author edits sections and never FHIR, and whose decision 7 said that anything
the surface needs and the API cannot give it is a gap in the API worth finding. This is the
first one it found.

## Context

The authoring surface renders a version handed to it and cannot fetch one. The API returns
`GET /fhir/Bundle/{id}/versions/{n}` as a FHIR document Bundle, and ADR-037 decision 2 says the
surface must never see a Bundle - not as a matter of taste, but because a surface that parsed
FHIR would be a second implementation of the content model, maintained by whoever last touched
the web tier.

So the surface needs a section-shaped view of a version, and something has to produce it.

The obvious answer is dangerous. A second representation of content, in a system whose whole
claim is that FHIR is the single source of truth, is exactly how a parallel model gets built by
accident: it starts as a view, acquires a field the Bundle does not have, and becomes the thing
people edit while the Bundle becomes the thing people export.

## Decision

**1. It is a projection: derived on every read, stored nowhere.** FHIR remains the single source
of truth (D1 Section 3.3). There is no table of sections, nothing to keep in step, and nothing
that can disagree with the Bundle because there is nothing that outlives the request.

**2. A save patches the version it was read from; it never rebuilds a Bundle from the
projection.** The projection carries what an author may change - a section's title and its
narrative - and a Bundle carries a great deal more. Reconstructing one from the other would
silently discard everything the projection does not model, which in this domain means
discarding regulated content because a web form did not have a field for it.

So the write path reads the version being edited, applies the changed sections to it by section
identity, and submits the whole Bundle through the ordinary write gate. Everything the author
did not touch is byte-for-byte what it was.

**3. A section the projection cannot represent is reported, not omitted.** The same rule the
surface applies to narrative it cannot parse, one layer down: a section missing from the
projection is a section an author will assume does not exist, and its absence from a save would
delete it. Such sections appear with their content marked unrepresentable and are refused as
edit targets.

**4. Section identity is the join, and it is never invented here.** A save names sections by the
identity the platform assigned (ADR-015, `SectionIdentity`). A section identity in a save that
is not in the version being edited is refused rather than added: adding a section is a different
operation from editing one, with different rules, and letting a save do it by accident is how a
label acquires a section nobody approved.

**5. The projection adds no authority.** It does not decide whether a version may be edited; it
reports what the platform decided. `editable` on the response is the answer the policy gave, and
the write is still refused by the write gate if the surface ignores it (ADR-037 decision 1).

**6. "Editable" means "may create the next version", not "may change this one".** ADR-037
decision 5 said an approved version is not editable and the surface should offer no editor for
one. Building this showed that framing to be wrong, and it is corrected here rather than left to
be discovered.

Every version is immutable, approved or not - saving never changes the version being read, it
mints the next one. Drafting a new version from an approved one is not an exception to
immutability, it is how a label evolves, and it is exactly what a regulatory author does most
often. So a surface that refused to open an approved version would be disabling something the
platform permits, which is the inverse of decision 1's rule and the more damaging direction: a
control the platform does not have, invented by the web tier.

What `editable` reports is therefore whether this caller may write to this document at all -
a scope and policy question - and not the state of the version in front of them. The surface's
job is to be clear that saving produces a new version, which is a matter of wording rather than
of permission.

## Alternatives considered

**Let the surface parse the Bundle.** Fewest moving parts, and it makes the web tier a second
implementation of the content model - one with no access to the profiles, the section identity
rules, or the cross-reference resolver. The first divergence would be silent and would surface
as content that fails validation for reasons the author cannot see.

**A FHIR `Composition.section`-shaped response, one hop from the Bundle.** Rejected for the same
reason as decision 2: it looks like a projection and behaves like the model, so it acquires the
model's obligations without the model's tests.

**Store the projection and reconcile.** This is the version of the mistake that is hardest to
undo, and it buys nothing here: the projection is cheap to derive and the read is not on any hot
path.

## Consequences

The API grows a second read path over the same content, which means two places that must agree
about what a version *is*. They agree because one is derived from the other on every request;
if that ever stops being true, this decision has been abandoned rather than refined.

Adding, removing and reordering sections is not expressible. An author can change what a section
says and not which sections exist. That is a real limit rather than an oversight - the section
set comes from the template (ADR-021), and changing it is a template-driven operation with its
own rules. Recorded as a debt.

Nothing here addresses the surface's inability to authenticate, which remains the reason it
cannot actually call any of this yet.
