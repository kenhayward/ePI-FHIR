# ADR-019: Lifecycle state model

Status: accepted
Date: 2026-08-13

Realises CAP-LCM-001, CAP-LCM-003, and CAP-LCM-007. Required by iteration 2
([iteration-2.md](../iteration-2.md) Section 7).

## Context

Iteration 1 built immutable version lineage and deliberately no states at all, on the argument
that an approval gate without segregation of duties and signature capture looks like a control
and is not one. Iteration 2 pays that debt, which means deciding **where state lives** relative
to content that is, by design, immutable.

Two constraints shape it. ADR-005 requires per-market regulatory-approval state to be modelled
separately from internal lifecycle state, because a version can be approved in one market and
not another. And ADR-015 makes versions immutable snapshots, so a state that changes cannot be
a property of the version itself without breaking that.

## Decision

**1. State is a record about a version, not a field on it.** A version is an immutable snapshot;
its state changes over time. Storing state inside the content would either mutate an immutable
artefact or create a new version for every transition, and neither is acceptable: the first
breaks reconstruction, the second makes the version history unreadable.

**2. Two state records, deliberately not one.** Internal lifecycle state (draft, in-review,
approved, superseded, withdrawn) is one record per version. Per-market regulatory-approval state
is one record per version **per market**. Conflating them cannot express "approved in Great
Britain, in review in the European Union" on the same content, which is the normal case rather
than an edge case.

**3. The state model is configuration, not code** (capability 21, ADR-012). States and the
transitions permitted between them are data, so a market or an organisation with a different
approval process is a configuration change. The engine knows how to apply a state model; it
does not know which states exist.

**4. A transition is a command that is validated, recorded, and emitted.** It is rejected unless
the model permits it from the current state, and unless the actor is permitted to make it
(capability 17). It records actor, timestamp, and reason to the audit trail, and emits an event.
There is no path that changes state without all three.

**5. Approval pins the content snapshot** (CAP-LCM-011): the version, the profile version it was
validated against, the template version it came from, and the master-data snapshot it relied on.
Reconstruction means reproducing the content *and* the context that made it valid, which is what
ADR-015 decision 8 already requires of an approved version.

**6. Transitions are append-only.** The state of a version at any past moment is derivable from
its transition history, rather than being overwritten by the current state. An audit trail that
records that something is approved, without recording when it became so and from what, cannot
answer the question an inspection actually asks.

## Alternatives considered

- **A state field on the version.** Simplest, and how most content systems work. Rejected: it
  mutates an immutable snapshot, or forces a new version per transition.
- **One state record covering both internal and regulatory state.** Rejected: it cannot express
  divergent per-market approval, which ADR-005 exists to preserve.
- **Hard-coded states.** Rejected: onboarding a market or an organisation whose process differs
  would become a code release, contradicting ADR-012 and capability 21.
- **Current state only, without transition history.** Rejected: reconstruction (CAP-LCM-006) and
  audit-trail review both need the path, not just the destination.

## Consequences

- Iteration 2 implements the state model loader, the transition engine, and the two state
  records. The engine is where segregation of duties is enforced for approval transitions.
- Because state is separate from content, a state change does not create a new version, and the
  version history stays a record of content changes rather than of process.
- Search (capability 15) filters on state records rather than content, so "awaiting approval in
  my market" is answerable without reading documents.
- Publishing (capability 14) reads the per-market approval state and effective date, not the
  internal state, so a label approved internally but not yet in a market cannot be published
  there by mistake.
