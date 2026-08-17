# ADR-022: Permission-scoped search and current-approved retrieval

Status: accepted
Date: 2026-08-14

Realises CAP-SCH-001, CAP-SCH-002, CAP-SCH-004 and the searchable half of CAP-SCH-003.
Required by iteration 2 ([iteration-2.md](../iteration-2.md) acceptance criterion 7).

## Context

Iteration 1 enforced scope on the read path with a decorator: fetch the document, ask the
policy engine, return it or return nothing. That works because a read names one document, so
"nothing" is a complete and honest answer.

Search breaks the assumption in three ways at once.

**A query names no document.** Something has to decide which of the corpus is a candidate
before any decision is made about an individual result, and the obvious implementation - search
first, filter afterwards - is wrong in a way that is easy to miss. A page of twenty filtered
down to three tells the caller that seventeen documents exist which they may not see. Result
counts leak the same information more directly, and paging becomes unusable: pages vary in
size, and "next page" cannot be computed from what the caller has been shown. Post-filtering is
not a scoping mechanism, it is a scoping mechanism with a side channel.

**A hit is not the content.** A search result reveals a document's existence, its title, its
market and its state. That is disclosure whether or not the content is attached, so hit
metadata has to sit behind the same gate as the content it describes.

**Search joins two owners.** The interesting queries cross content (capability 2) and lifecycle
state (capability 7) - "which labels are awaiting approval in my market" is the first question
anyone asks of a system like this. D3 forbids a service reaching another service's datastore,
so the join is a derived projection rather than a query across two stores.

The state names those queries filter on are configuration, not code (ADR-019). Search must not
know that "approved" is spelled `approved`.

## Decision

**1. Scope is a query predicate, never a post-filter.** The set of scopes the caller may read
is resolved first, and the query is executed within it. Everything downstream - matching,
counting, paging - happens inside the caller's scope, so counts are true counts and a page is
a full page.

**2. An unscoped search is not expressible.** The search port takes a `ScopedSearchQuery`,
which cannot be constructed without a permitted-scope set. There is no overload that omits it
and no flag that disables it: the wrong call does not compile rather than passing review.

**3. An empty permitted set returns nothing, not everything.** Stated as a decision because it
is the single most common way this class of code fails - an empty collection rendered into a
query becomes an absent predicate, and an absent predicate matches the corpus.

**4. The permitted scopes come from the same policy that decides a single read.** The resolver
enumerates the candidate scopes a caller could have - the cross product of the affiliates and
markets their identity asserts - and asks the policy decision point about each. It does not
reimplement the rule. Two implementations of one authorisation rule drift, and the one that
drifts unnoticed is the one that over-returns.

The cost is bounded by the caller's own breadth rather than by the size of the corpus, and the
answers are resolved once per request.

**5. The candidate set assumes a policy that narrows rather than widens.** A rule granting read
outside the affiliates and markets a caller asserts - an inspector role with sight of
everything, say - would not be found by enumeration, and such a caller would see nothing rather
than everything. That is the safe direction to fail, and it is recorded here because the fix is
to widen the candidate source, not to loosen the predicate. Partial evaluation of the policy
into a residual query (OPA's `compile` API) is the production answer and removes the assumption
along with the enumeration.

**6. Search is served from a projection, which is derived and never a source of truth.** It is
rebuildable from the canonical stores, it holds no state nobody else owns, and losing it loses
nothing. Today it is maintained synchronously in-process by decorators over the content and
lifecycle stores, so no write path can forget to update it; when it moves out of process onto
the event backbone it becomes eventually consistent, and decision 8 is what makes that safe.

**7. Current-approved is resolved per market from per-market approval state** (ADR-005), never
from a field on the content and never from internal lifecycle state. A version approved in
Great Britain and under assessment in the European Union is the normal case. The state that
counts as approved is named in the state model configuration, so search does not know how an
organisation spells it.

**8. Staleness may hide a result but must never reveal one.** Scope is stamped on immutable
content and a version's scope therefore never changes, so a stale projection cannot become
over-permissive about who may see what. State does change, so an answer that turns on state -
current-approved above all - is confirmed against the store that owns it before it is returned.
Search finds candidates; the owner of a fact answers for it.

**9. Results are bounded.** A page size is always applied, defaulted and capped, because an
unbounded query against a regulated corpus is an outage waiting for its first large tenant
(CAP-SCH-006).

## Alternatives considered

- **Post-filter the results of an unscoped query.** Simplest to write, and what the read path
  already does for a single document. Rejected: counts and paging leak, per decision 1. Worth
  noting that it is not insecure in the sense of returning content - it returns the right
  documents - which is exactly why it survives review.

- **Derive the predicate in application code from the subject's attributes.** Fast, no policy
  round trips, and a duplicate of `scope_covers_resource` living in C# where nobody tests it
  against the Rego. Rejected under ADR-012: authorisation logic lives in policy.

- **Partial evaluation of the policy into a query filter.** The right answer at scale and named
  above as the production path. Rejected for now because it means writing and trusting a
  translator from residual Rego to an index query, and a translator that mistranslates
  over-returns silently - the one failure mode this ADR exists to prevent. Enumeration is
  slower and wrong in the safe direction.

- **Query the FHIR server's own search API and let it scope.** Attractive because the content
  is already there and FHIR search is specified. Rejected: scope would then be enforced by
  whatever tags the query happens to carry rather than by the policy engine, the state join is
  not available at all, and it makes the canonical store the search index - which couples
  read-side performance to the system of record.

- **Index the content into the projection.** Rejected for now: the projection holds metadata
  and the text needed to match, and a hit is resolved to content through the existing scoped
  read path. One copy of the content is enough, and the copy that is not the source of truth is
  the one that goes stale in a way an inspection will ask about.

## Consequences

- Scope resolution costs one policy call per candidate scope per request. A caller with four
  affiliates and six markets costs twenty-four calls, cached for the request. This is
  acceptable now and is the first thing to replace under load, per decision 5.
- The projection must be rebuildable, and nothing may be written to it that is not derivable
  from the canonical stores. A rebuild path is not yet implemented and is recorded as a debt.
  What that debt costs was measured once the walkthrough began restarting the service
  (ADR-043): the index is in memory, so a restart empties it, and content that is still in the
  FHIR server becomes unfindable - permanently, because nothing reprojects. Every surface that
  reaches content through search, the authoring UI's label picker included, then shows an empty
  platform. The debt is not "search is slower than it could be"; it is "a restarted deployment
  looks empty".
- `product` as a search parameter binds to what the content carries as its subject until
  master data (capability 5) exists to bind it to properly. `effective date` waits for
  effective dating.
- Full-text search (CAP-SCH-003) is met at the level the projection supports - titles and
  section narrative - and not by a search engine. The dedicated index (OpenSearch, D3 Section
  12) is a later implementation of the same port, held to the same conformance suite.
