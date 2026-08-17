# ADR-047: Signing for a render template

Status: accepted
Date: 2026-08-17

Realises CAP-TPL-008 and CAP-AUD-003. Repairs [ADR-042](0042-template-store.md) decision 3, whose
approval gate was configured and could not be passed.

## Context

[ADR-042](0042-template-store.md) decision 3 put render templates through the lifecycle engine
labels use, "the same segregation of duties, the same signature gate - because a template
determines what a patient reads, and approving one is a regulatory act with a named accountable
person behind it".

The gate was built and nobody could pass it. Two things stood in the way, and both were
invisible for the same reason: **the tests asserted the refusals, and a gate nobody can pass
refuses everything correctly.**

1. **A signature could only be made over a FHIR Bundle.** `POST /signatures` read the content
   store, and a template is a stylesheet that lives in the template store - so every attempt to
   sign for one answered 404. Measured against the running stack, not inferred.
2. **Approval demanded an approval context and none could be supplied.** The engine refuses an
   approval it cannot pin (ADR-024 decision 3), and the template endpoint passed nothing.

So a template could go draft to in-review and no further. No template could be approved, and
because only an approved template may produce an official render (ADR-033 decision 2), nothing
could ever be officially rendered. Three pull requests of template machinery led to a state
nobody could leave.

## Decision

**1. The signing service signs a `SignableArtefact`: an identity, a version, and a hash.** The
FHIR overload is written in terms of it. Every route to a signature still comes through one
service, for the reason every route to a state change comes through one engine - a control
enforced in one place and not another is not a control.

**2. Whoever owns the artefact computes its canonical form.** `TemplateCanonicalForm` lives in
the template module, because only it knows what an approver is signing for. The signing service
knows about credentials and manifests, and putting "what a template means" inside it would give
it a second job it is not qualified for. `ContentHash` gains an overload over bytes, so a later
change of algorithm reaches everything the platform has ever signed rather than one artefact
kind.

**3. What an approver signs for is the identity, the version, the name, and the stylesheet.** The
stylesheet because it is what changes the document; the name because it is what they read when
deciding whether to sign; identity and version so a signature cannot be carried to another
template that happens to look the same.

The encoding is length-prefixed rather than delimited. A separator can appear inside a
stylesheet, and a canonical form that can be forged by moving a character from one field into the
next is a hash with a collision anybody can construct. It is readable rather than packed, so what
was signed can be shown to somebody and not only compared.

**4. A template approval is pinned against its own canonical hash.** The same question a label's
pin answers - what was this approved against - with the honest answer for a stylesheet.

No conformance packages, and that is not an omission: a template is not validated against the ePI
implementation guide, so naming packages would record a check that never happened. No template
either, because that field says which template a *label* was instantiated from, and this is the
template.

**5. The request says which kind of artefact it is signing for, and an unknown kind is refused.**
Defaulting to content on an unrecognised value would hash a label the caller never named and hand
back a signature over something else entirely.

## Alternatives considered

**Drop the signature requirement from the template state model.** One line of configuration, and
it removes the only thing that makes template approval mean anything. ADR-042 decision 3 was
explicit that a template gets the same gate as a label, and the reason has not changed: a
template determines what a patient reads.

**Write a FHIR resource to stand for the template so the existing path works.** Rejected under
ADR-042 decision 1 for the same reason it was rejected there - there is no resource that means a
stylesheet, and inventing one to reuse a code path is asserting that a stylesheet is ePI content.

**Sign over the template's identifier and version alone.** Simple, and it signs for nothing: the
stylesheet could change underneath the signature and the hash would still match. The point of a
hash is that it covers what was approved.

## Consequences

A template can be approved, which unblocks the official render (ADR-033 decision 2) and the
removal of the preview scaffolding. That is now the next piece of work rather than a blocked one.

`SignatureCheck` compares a manifest's document *value* with the version being transitioned and
not its identifier system, so in principle a template signature would satisfy a label transition
of the same identifier and version. It cannot happen in practice - label identifiers are minted
UUIDv7 into the document system and template identifiers are configured slugs, and the two sets
are disjoint - but it holds by convention rather than by construction. Making it rigorous means
the lifecycle engine knowing what kind of artefact a version is at check time; the registration
already records the kind (ADR-042), so the missing piece is a store query. Recorded here rather
than done, because the change touches a port and its conformance suite and this pull request is
already repairing a gate.

The lesson that generalises: **a test suite that only asserts refusals cannot tell a working gate
from an impassable one.** The template tests were thorough about what is refused and never once
went through. Every gate this platform builds should have at least one test that passes it.
