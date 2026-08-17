# ADR-044: Rebuilding the search projection at start-up

Status: accepted
Date: 2026-08-17

Realises CAP-SCH-001. Pays the debt [ADR-022](0022-permission-scoped-search.md) recorded and
[ADR-043](0043-durable-template-storage.md) priced.

## Context

[ADR-022](0022-permission-scoped-search.md) decision 6 says the search projection is derived and
never a source of truth. Its consequences noted that "a rebuild path is not yet implemented and
is recorded as a debt" - a sentence that sat there for two iterations because nothing made its
cost visible.

Then the walkthrough began restarting the service (ADR-043) and the cost turned out to be the
whole platform. The index is in memory; a restart empties it; nothing reprojects. On the
development stack that meant **73 documents in the FHIR server and zero search results** -
permanently, because only a write re-projects and nothing was going to write them again. Every
surface that reaches content through search shows an empty platform, the authoring UI's label
picker included. The content was never lost. It was simply unreachable by every route anybody
uses.

"Derived and never a source of truth" is not a description; it is an obligation. Whatever is in
the projection has to be reconstructible from what is canonical, and until something
reconstructs it, the claim is untested.

## Decision

**1. The projection is rebuilt from the canonical stores at start-up, before the service
serves.** A search answered from a half-built index is a wrong answer rather than a slow one, so
this happens during start-up rather than in the background.

**2. The lifecycle store says which versions exist; the content store says what they say.** A
version is registered before its content is written (ADR-025), so nothing that reached the
content store is missing from the lifecycle store. Nothing reads the index to decide what to do,
so a rebuild does not depend on the state it is repairing.

**3. A registration with no content behind it is not projected, and is counted.** That is an
inert registration (FN-LCM-008): there is no title, no scope and no language to index. A hit with
no scope is worse than no hit, because scope is what keeps a result away from somebody who may
not see it. The count is logged with a pointer to the reconciliation report - the rebuild is the
second place these become visible, and the first place many operators will look.

**4. A rebuilt version carries the state it reached, not the state it started in.** Otherwise a
search for what is approved answers with what was approved before somebody last restarted the
service. Where the lifecycle store lists a version as registered and has no state for it, the
rebuild raises rather than defaults: a default would put every affected version into the index
under a state nobody moved it to, which is a wrong answer wearing the shape of a right one.

**5. Templates are not projected.** The lifecycle engine manages render templates too (ADR-042
decision 3), and a template is not a label - it has no scope, so a search returning one would be
returning a result no permission decision could be made about.

**6. The rebuild reads everything, not everything before now.** The lifecycle store's only
enumeration takes a cutoff because the inert-registration report needs a settle period. A rebuild
does not: a registration made a moment ago is as much a thing to project as one made last year.

## Alternatives considered

**Reproject lazily, on the first search that misses.** Cheaper at start-up and wrong in the way
that matters: a search that misses is indistinguishable from a search with no results, so the
trigger for the repair is the same event as a correct empty answer. It would also mean the first
caller after a restart pays for the whole corpus.

**Persist the index instead - wire the OpenSearch already in the stack.** The right destination
and a larger change: a second implementation of `ISearchProjection` and `ILabelSearch`, held to
the same conformance suite (ADR-022 consequences). It does not remove the need for this. A
durable index still drifts, still needs reconstructing after a schema change, and still has to
be derivable to be trusted. Rebuild first, persist second.

**Treat the index as canonical and back it up.** Rejected on ADR-022 decision 6, and worth
restating: a projection that cannot be rebuilt is a second source of truth that nobody has
declared, and the first disagreement between it and the content store would have no arbiter.

## Consequences

Start-up cost is proportional to the number of registered versions - one content read and one
state read each. On the development stack, 73 versions rebuild in under a second. This is
acceptable now and is the first thing to replace under load: a corpus of a hundred thousand
versions wants a durable index (the alternative above) rather than a longer start-up.

The walkthrough asserts it rather than noting it. It restarts the service, then asks for content
by identifier and for versions by state, and both answer - which is the check that would have
caught the original omission had a rebuild ever been expected.

One walkthrough check had to be narrowed to stay honest: it used to ask for the first page of
everything approved and look for the label it had just made. That held only while the index was
as empty as the walkthrough had left it. A deployment that has been used has more than a page of
approved versions, so the check now asks the question it always meant - is *this* label findable
as approved.

Nothing here addresses ordering, and this is where the correction belongs: the order was
defined - identifier ascending, then version descending - and it was the wrong question
answered stably. Identifiers are time-ordered, so every page led with the oldest label in the
deployment. Invisible while the index held only what one walkthrough had written; settled in
[ADR-045](0045-search-result-order.md).
