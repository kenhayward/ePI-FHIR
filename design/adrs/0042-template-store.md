# ADR-042: Where templates live, and how they are approved

Status: accepted
Date: 2026-08-16

Realises CAP-TPL-001, CAP-TPL-007 and CAP-TPL-008. Completes [ADR-021](0021-template-representation.md),
which settled what a template *is* and left where one lives open, and unblocks
[ADR-033](0033-rendering.md) decision 2, whose consequence is that nothing can produce an
official render until an approved render template exists.

## Context

Two kinds of template exist as types with nothing to hold them. `LabelTemplate` says what sections
a kind of label has (ADR-021); `RenderTemplate` says how a label is rendered (ADR-033). Neither
has a store, so neither has a version anybody can refer to, and the consequences have accumulated:

- The preview renders with scaffolding, and says so, because there is no approved render template
  to use instead.
- Nothing is written to the asset store, because an artefact made with scaffolding is not one to
  file.
- CAP-TPL-008's template lifecycle - draft, in-review, approved, retired - has nowhere to happen.

Both ADR-021 and ADR-033 decision 2 already say what a template is: content, versioned, immutable
per version, approved by a regulatory owner, because **a template determines what a patient
reads**. What is unsettled is where that content sits and what approves it.

## Decision

**1. Templates live in their own store, not in the FHIR content core.** The content core holds
canonical ePI content, and a render template is a stylesheet - it is not FHIR, there is no
resource that means it, and putting it there would mean inventing one. A label template is closer
to FHIR and still is not a label: it is a description of what a label should contain.

**2. A template version is immutable, and a change is a new version.** The same rule as content,
for the same reason: a render keyed to template version 2 must mean the same thing in five years
as it did when it was filed (ADR-033 decision 1).

**3. Approval uses the lifecycle engine labels use - the same engine, the same mechanisms, its
own states.** Not a second approval mechanism.

The wording here originally said "the same state model", and that is wrong in a way worth
correcting rather than glossing: a label is superseded and a template is retired, and a template
has no per-market approval at all. What is shared is the engine, the segregation-of-duties check,
the signature gate, and the fact that the states are configuration - `config/lifecycle/template-states.json`
sits beside the label's and is read by the same loader.

This is the decision the rest rests on. The lifecycle engine works on a `VersionRef`, which is an
identifier and a version and nothing about labels; the states are configuration; the signature
gate is configuration. A template can therefore be drafted, submitted, and approved by somebody
who did not write it, under signature, with none of that written twice.

A second approval mechanism would be a second set of rules to keep in step with the first, and
the one that drifts is the one that decides whether something a patient reads was approved.

**4. Only an approved template version may produce an official render.** A render made with a
draft template is a preview, whatever else is true of it - which is what the preview already
says, and this is the rule it will keep saying it by.

**5. Templates are not configuration.** Stated because it is the natural wrong turn and this
repository leans hard the other way: markets, routing, state models and identifier authorities
are all config-as-data, and a template looks like one more file to drop in.

It is not, and ADR-012's own reasoning says why. Config-as-data exists so that a new market or
rule needs no code release - it is an administrative act. Approving what a patient reads is not
an administrative act; it is a regulatory one, with a named accountable person and a signature.
A template under `config/` is a template an administrator can change, and the whole point of
decision 3 is that they cannot.

**6. The store is held to a conformance suite, as every store here is.** One suite, run against
the in-memory reference implementation and against whatever durable store follows, so the two
cannot drift.

**7. A deployment may be seeded with standard templates, and they arrive as drafts.** Amended
after the fact: the consequences below originally said that seeding one at install would be
seeding an approval nobody gave. That is true of seeding an *approved* template and not of
seeding a draft, and the distinction is the whole of it.

A draft template is one nobody may render officially with, so what a seed supplies is a starting
point rather than a decision. An adopting organisation gets QRD-shaped templates to work from,
reviews them, changes what does not suit, and takes them through the same gate as anything else -
which is exactly what they would do with one they wrote themselves, minus a blank page.

The seeds live under `config/templates/seed/`, and that is not a contradiction of decision 5.
What decision 5 forbids is an administrator changing what a patient reads; what these change is
what a *draft* starts as, and a draft reaches a patient only by being approved by somebody
accountable. Editing a seed cannot put unapproved words in front of anybody, because the gate is
untouched. Templates already approved are unaffected by a seed changing: seeding never rewrites
what exists.

## Alternatives considered

**Store templates as FHIR resources in the content core.** Attractive for the reuse - versioning,
identity and immutability are all there already - and rejected under decision 1: a render
template has no FHIR resource that means it, so this would mean inventing one and asserting it is
ePI content. `Composition` and `Questionnaire` have both been suggested for template-shaped
things in other projects, and both mean something else.

**A template lifecycle of its own, simpler than a label's.** Tempting because a template has
fewer states in practice. Rejected under decision 3: the temptation is exactly how the second
mechanism gets weaker than the first, and the segregation-of-duties rule is the same rule whether
the artefact is a leaflet or the thing that shapes one.

**Keep templates in `config/` and treat approval as a review of the pull request.** Honest for a
demonstration and wrong for the claim the platform makes. Git history is not a Part 11 signature,
and "approved because a maintainer merged it" is not attributable to a named person asserting
what they were asserting.

## Consequences

The lifecycle state model is now applied to something other than a label, which is a
generalisation rather than a change - the engine never knew what a `VersionRef` referred to, and
that this went unnoticed until now is a point in its favour.

An adopting organisation gets standard templates as drafts and no approved template at all until
somebody signs for one. A fresh deployment therefore cannot render anything officially on the
day it is installed, which is correct: the first official render should follow a person deciding
that a template is fit to shape what patients read.

The preview scaffolding stays until the surface can author templates, and its removal is what
tells us this is finished rather than merely built.

Nothing here addresses variant templates deriving from a core template (CAP-TPL-006) or listing
labels impacted by a template change (CAP-TPL-009). Both need this first.
