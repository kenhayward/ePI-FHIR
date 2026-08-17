# ADR-045: What order search results come back in

Status: accepted
Date: 2026-08-17

Realises CAP-SCH-001 and CAP-SCH-006. Settles what [ADR-044](0044-search-projection-rebuild.md)
recorded as the next thing search owed.

## Context

Search returned results ordered by document identifier ascending, then version descending. That
is deterministic, total, and stable - and useless, in a way that only became visible once the
index held more than one walkthrough's worth of content.

Identifiers here are time-ordered UUIDs (ADR-017), so identifier-ascending is
oldest-created-first. Every page therefore led with the oldest label in the deployment, and the
one an author had just saved was on the last page. A picker shows the first twenty. The
walkthrough caught it as a failing assertion the moment [ADR-044](0044-search-projection-rebuild.md)
rebuilt a real corpus into the index: the label the run had just created and approved was not on
page one of what was approved.

Nothing was wrong with the ordering as a rule. It was answering a question nobody asks.

## Decision

**1. Results come back most-recently-touched first.** Touched means written or moved: a new
version, and a transition to another state. An author looking for what they were working on
finds it at the top, and somebody who has just submitted a label for review finds it where they
left it rather than where it was created.

**2. The order is total, and the tie-break is the old rule.** Most recent first, then identifier
ascending, then version descending. Content written in one batch - a rebuild, an import - shares
a moment, and without a total order the page boundary is whatever the store felt like: a caller
paging through can see one version twice and another never. That is asserted directly, by paging
through and counting.

**3. When something was touched is projected, not decided by the index.** `ISearchProjection`
takes the moment as an argument. An index stamping its own clock would be unable to reproduce
anything: a rebuild would give a whole corpus one moment and lose the order it exists to
restore.

Each caller has a truthful moment to give. A content write is happening now. A lifecycle
transition carries the moment it was recorded, which is evidence rather than a reading of a
clock. A rebuild reconstructs it from the version's last transition, or from its registration if
it has never moved.

**4. There is no sort parameter.** One order, chosen for the question people actually ask.
Sorting by title or by state is a real request and a different decision, and adding a parameter
before anybody has asked for one is an API surface with no user and a second order to keep
correct.

## Alternatives considered

**Sort by identifier descending - newest created first.** One character of change, no moment to
project, and wrong for the same reason as the original: it orders by when a label was *created*,
so a label authored last year and edited this morning sits at the bottom. What an author is
looking for is what they last touched.

**Sort in the API rather than the index.** Would work only within a page, which is the one place
it cannot help: the page is already the wrong twenty. Ordering has to be part of the query, not
of the response.

**Keep the order and fix the picker with a filter.** Suggested by the failing walkthrough check,
which was narrowed to search by identifier (ADR-044 consequences). Fine for a check that knows
which label it wants and no use to a person opening a picker to see what they were doing.

## Consequences

`ISearchProjection` gains a required parameter on both methods rather than an optional one. Every
call site now has to say when, which is the point: a default would let a caller with no truthful
moment quietly claim now, and a rebuild is exactly such a caller.

The dedicated index that replaces the in-memory one later inherits this through the shared
conformance suite, which now asserts the order and the paging consequence. A search engine has
its own idea of relevance ordering, and this is the assertion that stops it arriving silently.

The walkthrough asserts that the label a run has just worked on leads the first page - the check
that would have caught the original problem, had anybody thought to ask.

Recency is not relevance. A free-text query still returns most-recently-touched first rather
than best-matching first, which is defensible for a corpus this size and is the next thing to
revisit when full-text search moves to a real engine (ADR-022 consequences).
