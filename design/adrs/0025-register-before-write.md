# ADR-025: A version is registered before its content is written

Status: accepted
Date: 2026-08-14

Realises CAP-LCM-001 and CAP-IAM-006 more completely. Pays the half of the debt ADR-024
decision 8 recorded as still open ([iteration-3.md](../iteration-3.md) Section 2.3, delivery
row 1b).

## Context

Storing a new version is two writes to two systems: the content goes to the FHIR content store,
and the registration - who authored it, in what state, when - goes to the governance store.
ADR-024 made the writes that share a database atomic. This pair does not share one, and a
distributed transaction across a FHIR server and PostgreSQL would be the wrong answer even if
it were available.

So the question is not how to make it atomic. It is **which way round to fail.**

Today the content is written first and registered afterwards, so a failure leaves **content
nobody is recorded as having authored**. That is the worse of the two possible outcomes by a
distance: the author is what segregation of duties is checked against (CAP-IAM-006), so
unregistered content is content that can never be approved, never be reasoned about, and yet
exists and is readable through every read path. It is ungoverned content inside a system whose
entire claim is that content is governed.

The other way round leaves a registration for content that was never written. That record
refers to nothing: every read returns not-found, every transition refuses because scope is
decided on the content, and nothing can be done with it. It is inert.

There is a second thing in the way. The content store mints identity itself, inside the write.
Nothing can be registered before the write because until the write happens there is no
identifier to register.

## Decision

**1. Identity is minted before the content is written, by the caller.** `CreateAsync` takes the
identity it is to store under rather than inventing one. This is what makes the ordering
possible at all, and it is honest: a document's identity is the platform's to mint (ADR-015),
not the storage layer's to decide as a side effect.

**2. A version is registered under lifecycle management before its content is stored.** A
failure then leaves an inert record rather than ungoverned content.

**3. Registration is a decorator, positioned inside the gates and outside the store.** Auditing,
publishing, validation and scope all sit outside it, so content that is invalid or that the
caller may not write never reaches registration and cannot leave a record behind. The store
itself sits inside it, so nothing is written that was not registered first.

**4. A new version states the version number it expects, and the store refuses a mismatch.**
The version has to be known before the write for the same reason the identity does. Making the
caller state it turns two authors racing to create version 4 from a silent interleave - where
one write lands on top of a version number the other also believed it had - into a refusal that
names the conflict.

**5. An inert registration is left alone.** It is not cleaned up, not compensated, and not
hidden: the lifecycle store is append-only, and a record that could be removed on the strength
of a failed write is a record that could be removed. It is detectable - registered, with no
content at any version - and a reconciliation report is the right place to surface it, not a
delete.

## Alternatives considered

- **Leave the order as it is and reconcile afterwards.** A pass could find content with no
  registration and register it from the audit trail, which does record the actor. Rejected as
  the primary mechanism: it puts an author on a version by inference, after the fact, and
  segregation of duties then rests on a reconstruction rather than on a record made at the time.
  It also cannot run in a deployment whose audit trail is held elsewhere.

- **Two-phase commit across the FHIR server and PostgreSQL.** Not available through the FHIR
  REST API, and it would couple the availability of the content store to the availability of the
  governance store in both directions.

- **An outbox: write content, and enqueue the registration durably.** The standard answer, and
  the right one when the second write can be retried until it succeeds. Rejected here because
  the window it closes is the wrong one - between the content write and the outbox write there
  is still ungoverned content - and because it adds a queue, a worker and an at-least-once story
  to close a gap that reordering closes outright.

- **Keep minting inside the store and pass the identity back out before committing.** There is
  no "before committing" in a REST call to a FHIR server.

## Consequences

- `IContentStore` gains the identity and the expected version as parameters, and loses the
  minting side effect. Every implementation and decorator changes; the conformance suite gains
  cases for the mismatch refusal.
- A caller that mints an identity and then fails to write leaves that identifier unused. Unused
  identifiers are free - they are UUIDs - and nothing infers meaning from a gap.
- Inert registrations accumulate at whatever rate writes fail after registration, which is the
  rate at which the content store is unavailable mid-request. Worth a reconciliation report
  before that rate matters; recorded as a debt rather than built now.
- Concurrent creation of the same next version now fails loudly for one of the two callers.
  That is a behaviour change, and the right one: it was previously silent.
