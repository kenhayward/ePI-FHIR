# ADR-043: Durable template storage, and start-up as an ensure rather than a create

Status: accepted
Date: 2026-08-17

Realises CAP-TPL-001 and CAP-CFG-004. Completes [ADR-042](0042-template-store.md), which put
templates in their own store and left that store in memory, and follows
[ADR-024](0024-atomic-governance-writes.md) on what start-up may assume about a database.

## Context

[ADR-042](0042-template-store.md) gave render templates a store of their own and put their
approval through the lifecycle engine. The lifecycle engine is durable; the template store was
not. Half of a template's identity was written to PostgreSQL and the other half lived in the
process.

The consequence was not subtle. **The API did not survive a restart.** On the second start the
template store was empty, so seeding recreated all three standard templates and registered each
one again - against a lifecycle table that already held them:

```
Npgsql.PostgresException: 23505: duplicate key value violates unique constraint
"lifecycle_version_pkey"
```

Nothing caught it. Every test of the API ran on in-memory stores, where the two halves are
consistent because both are lost together; the walkthrough runs against a service that is
already up and never restarts it. The deployment looked healthy right until somebody restarted
it, which is the first thing anybody does.

Underneath the missing durability sits a second problem the fix has to answer. Seeding writes to
the template store and registration writes to the lifecycle store, and there is no transaction
across the two. A process that dies between them leaves a template no lifecycle record knows
about - and "register what I just created" never looks at it again, because the next start
creates nothing.

## Decision

**1. The template store is durable wherever the governance store is.** `PostgresTemplateStore`,
selected by the same connection string that selects every other durable store, falling back to
the in-memory implementation when there is none. Held to `TemplateStoreConformance` as shared
source, so the two implementations answer the same questions the same way - the 11a/11b
precedent, unchanged.

**2. A template version is a row, and the table is append-only.** Primary key of identifier and
version, no update, no delete, and a trigger that refuses both from any connection. The same
guarantee the other governance tables carry and for a sharper reason: a render filed against
template version 2 is only reproducible while nobody can edit version 2 (ADR-033 decision 1).

**3. Start-up ensures rather than creates, and the lifecycle store decides.** Seeding reports
what it created *and* every standard template now present; start-up registers those the
lifecycle store has no record of. Both steps are then idempotent independently, and neither
depends on the other having completed.

This is what makes the crash-in-between case recover. A template written without a registration
is registered on the next start, and an operator is told it happened - because nothing being
wrong *now* does not mean nothing went wrong.

The alternative was to make the pair atomic. There is no transaction spanning two stores, so
atomicity here would mean a two-phase protocol or one store - and the platform's own rule is
that no service reaches another's datastore. Idempotence on both sides costs two queries at
start-up and needs no coordination.

**4. Registration is not skipped for a template somebody else authored.** The check is "does the
lifecycle store know this version", not "did the platform author it". A standard template that a
person registered themselves is already registered, and start-up leaves it alone.

## Alternatives considered

**Keep the template store in memory and stop seeding on start-up.** Removes the crash by
removing the write. Rejected: it also removes the templates, and a deployment that comes up with
nothing to author against is not a deployment. The problem was never that start-up writes; it
was that the write was not idempotent.

**Delete the offending lifecycle rows to let the service start.** Available, immediate, and
wrong. Those rows are governance records of an append-only store, and deleting evidence to make
a service start is the failure mode the append-only trigger exists to prevent.

**Put templates in the content core after all, to reuse its durability.** Rejected again, on
ADR-042 decision 1's reasoning: a render template is a stylesheet, there is no FHIR resource
that means one, and needing a table is not a reason to assert it is ePI content.

## Consequences

Templates survive a restart, which sounds small and is the difference between a demonstration
and a system. Renders filed against a template version stay reproducible, because the version
they name is still there and cannot be edited.

`Epi.Api.IntegrationTests` gains the case that would have caught this: start the API, dispose it,
start it again against the same database. It joins the class of tests that exist because the
in-memory stores made a whole category of defect invisible - and this class has now caught three
things CI could not.

The walkthrough gains a restart of its own, because the blind spot it had was anything that only
goes wrong the second time - and it had that blind spot while being the thing this repository
trusts to find what CI cannot. It now restarts the API container and asks the questions again.

Nothing here addresses authoring a template through the surface (CAP-TPL-005) or the official
render an approved template unlocks (ADR-033 decision 2). Both need this first.
