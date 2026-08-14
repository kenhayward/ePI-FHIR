# ADR-024: Atomic governance writes, and a migrated schema to hold them

Status: accepted
Date: 2026-08-14

Realises CAP-LCM-011 more completely and supports CAP-AUD-002. Pays two debts recorded across
iterations 1 and 2 ([iteration-3.md](../iteration-3.md) Section 2.3), and is required before
this iteration adds columns to the governance tables.

## Context

Two failures were recorded rather than fixed, and both are about a write that is really two
writes.

**An approval records a transition and pins what the version was approved against** (ADR-023).
Today the transition is appended, and then the pin is written. If the second fails there is an
approved version with no record of what it was approved against - which is precisely the
evidence ADR-023 exists to produce, missing in exactly the case it matters.

**A new version is stored and then registered under lifecycle management.** If registration
fails there is content nobody is recorded as having authored, and the author is what
segregation of duties is checked against.

These look like the same problem and are not, which is why one ADR settles one of them. The
transition and its pin are **two rows in one database**. The content write and its registration
cross the FHIR content store and the governance store - two systems, no shared transaction, and
a distributed transaction across them would be the wrong answer even if it were available.

There is a third thing in the way. The governance schema is created by `CREATE TABLE IF NOT
EXISTS` at start-up, which does nothing at all to a table that already exists. A column added
later never appears in a database that predates it, and the first write afterwards fails. CI
cannot see this: it starts from an empty database every time. It has already happened once, and
this iteration changes every governance table.

## Decision

**1. A transition and its pinned context are written in one transaction.** They are rows in one
database; there is no reason for them to be two operations, and every reason for them not to be.

**2. Pinning without recording the transition that pinned it is not expressible.** The pin is
not a separate call the caller may forget or reorder: the store's append takes the transition
and, optionally, the context to pin with it, and there is no other write path to a pin. The
same reasoning as ADR-022 decision 2 - the wrong call should not compile rather than not pass
review.

**3. The engine assembles the pin; the caller supplies only the ingredients.** The lifecycle
engine knows when a transition lands on the approved state and what the transition's own
timestamp is; it does not know about FHIR bytes or conformance packages. So the caller passes a
content hash, the packages, the identifier authority and the template - and the engine decides
whether a pin is due and builds it. Neither side has to know the other's business, and the
decision about *when* to pin lives with the thing that knows.

**4. A model that names an approved state must be given something to pin, or the approval is
refused.** The alternative is an approval that silently records no context, which looks exactly
like an approval that recorded one until somebody asks. The same shape as ADR-019's refusal to
start without a signature check.

**5. The governance schema is applied as ordered, recorded migrations.** Each migration has a
stable identifier, runs inside its own transaction, and is recorded in a ledger when it
succeeds. A migration already in the ledger is never run again; a migration that fails leaves
the database as it was and names itself in the error.

**6. The migration ledger is append-only, like everything else here.** It is not GxP evidence
about a label, but it is the record of what was done to the store that holds the evidence, and
D3 Section 10.3 wants a deterministic, traceable release. It costs one trigger.

**7. Existing databases are migrated, not recreated.** The first migration is the schema as it
stands today, written so that it is a no-op against a database that already has it; every
subsequent change is additive and idempotent. A deployment that has data keeps it.

**8. Registration is not solved here.** It crosses two systems and needs a different mechanism
and its own decision. Recorded as still open rather than quietly folded into this one, because
the two halves of the debt were written down together and someone will otherwise read this as
closing both.

## Alternatives considered

- **Leave the pin as a second write and reconcile later.** A reconciling pass could find
  approved versions with no pin and write one - from what? The packages in force *now*, which is
  the exact substitution ADR-023 decision 4 refuses. A pin that cannot be reconstructed
  faithfully cannot be reconciled, only invented.

- **A unit of work spanning every governance store.** More general, and it would also cover
  writes that have no reason to be atomic - a signature and a transition are deliberately
  separate acts, and binding them would make one impossible without the other. Generality here
  buys coupling.

- **Keep `PinAsync` on the pinned-context store as well as the atomic path.** Convenient for
  tests and for a future caller. Rejected: a second way to write a pin is a way to write one
  without a transition, and it would be used exactly once, by accident, in production.

- **A migration framework (EF Core Migrations, DbUp, Flyway).** Each is capable and each brings
  a dependency, a tool, and a conventions layer for what is currently a few hundred lines of
  DDL. Revisit when the schema justifies it; the ledger and the runner here are deliberately
  small enough to read in one sitting, and swapping them for a framework later is a contained
  change.

- **Drop and recreate in development, migrate only in production.** Two schema paths, one of
  which is never exercised where it matters. The path CI runs must be the path a deployment
  runs, or CI is testing something else.

## Consequences

- `ILifecycleStore.AppendAsync` gains the context to pin. `IPinnedContextStore` becomes
  read-only, which is what the rest of the platform wanted from it anyway.
- `LifecycleService.TransitionAsync` gains an approval context and refuses an approval without
  one where the model names an approved state. Every caller that approves must now say what the
  version is being approved against, which is the point.
- Store `InitialiseAsync` methods are replaced by one schema application, so there is one place
  that knows what the governance schema is, and it is ordered.
- A failed migration is a start-up failure. That is intended: a service running against a schema
  it could not fully apply is a service whose writes may or may not land.
- Registration remains non-atomic and is recorded as such. It is the next pull request, not a
  someday.
