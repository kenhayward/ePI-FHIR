# ADR-018: Audit event contract

Status: accepted
Date: 2026-08-13

Realises CAP-AUD-001 and CAP-AUD-002, and the D2.5 cross-capability note on keeping the audit
contract uniform. The third of the three ADRs iteration 1 identified.

## Context

Capability 19 is the evidentiary record every other capability writes to. If each capability
invents its own event shape, the audit trail becomes a collection of formats that an inspector,
and any reconstruction or export, has to reconcile. The contract is worth fixing before there
are several writers rather than after.

ALCOA+ frames what a record must be: attributable, legible, contemporaneous, original, and
accurate. Those are properties of the shape and of who fills it in, not of the storage.

## Decision

**1. One record shape for every capability**: actor, action, target, outcome, recorded-at, and
an optional before, after, and reason. Actions are dotted and namespaced by the thing acted on
(`content.create`, `access.read`), so the trail can be filtered by area without parsing.

**2. The sink stamps the time, not the caller.** A contemporaneous record is one the system
timed; a caller-supplied timestamp is a claim rather than evidence, and a wrong clock in one
service would otherwise corrupt the ordering of the whole trail.

**3. Denials and failures are recorded as deliberately as successes.** An audit trail
containing only what worked cannot answer what was attempted, which is where an investigation
usually starts. Access decisions record **allow** as well as **deny** (CAP-IAM-009): a trail of
refusals alone shows attacks but not misuse by someone entitled to be there, which is the
harder case in a regulated system.

**4. Before and after are the content, not a description of the change.** A diff computed at
write time is an interpretation; the two states are the evidence, and any diff can be recomputed
from them later.

**5. Immutability is a property of the interface.** `IAuditSink` has no update and no delete, so
no code path can exist that uses one. Reads return copies, so a reader cannot alter history by
accident. Disposition at end of retention belongs to capability 22, not to any caller.

**6. Auditing is a decorator, never a call inside business logic.** It cannot then be forgotten
in a new code path, and the thing being audited does not have to know it is being audited.

## Alternatives considered

- **A per-capability event shape**, richer for each. Rejected: reconstruction and export
  (CAP-AUD-004, CAP-AUD-007) would need to understand every variant, and each new capability
  would extend that burden.
- **Recording only state-changing operations.** Rejected: CAP-IAM-009 requires access decisions,
  and reads of regulated content are themselves of interest to an inspection.
- **Storing a computed diff instead of before and after.** Rejected as above: an interpretation
  cannot be re-derived into the original, but the original can always be diffed.
- **Letting callers supply the timestamp** for accuracy at the point of action. Rejected: it
  makes the trail's ordering depend on every caller's clock.

## Consequences

- Every writer uses one shape, so inspection search, reconstruction, and export are written once.
- The audit trail contains failures and denials, which makes it larger and more useful.
- Before and after hold full content, so audit storage grows with content size. That is the
  intended trade for reconstructability, and retention (capability 22) is where it is managed.
- The durable sink is an append-only table with WORM export (D3 Section 3.1, ADR-013). The
  in-memory implementation here is the reference the same tests hold both to.
- Audit records reference the identifier systems from ADR-017 and are permanent, which is why
  that authority is configuration and must be set before records worth keeping exist.
