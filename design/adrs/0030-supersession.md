# ADR-030: Supersession

Status: accepted
Date: 2026-08-15

Realises CAP-LCM-005 and the retrievability half of CAP-LCM-006. Required by iteration 3
([iteration-3.md](../iteration-3.md) acceptance criterion 5, delivery row 6).

## Context

Approving version 4 of a label says something about version 3: it is no longer the one the
organisation stands behind. Until now nothing said it. The state model has a `superseded` state
and a `supersede` action, and reaching them was left to a caller remembering to.

That leaves a window in which two versions of the same label are both `approved`, which is not
a state the organisation is ever actually in. Anything reading "the approved version" during
that window gets an answer that depends on which it looked at first.

ADR-029 has already answered this for markets: a version is superseded in a market when a later
one takes effect there, derived rather than recorded. What remains is the organisation's own
statement about its own content, which is a different fact and belongs on the internal side
(ADR-005).

## Decision

**1. Approving a version supersedes the previously approved version of the same label.** Not as
an inference drawn later, but as a transition recorded at the moment, with its own actor and
timestamp and a reason naming the version that displaced it.

**2. It is recorded, not derived, because internal state is a statement rather than a
calculation.** The market side is derived because effect is a function of dates that pass
without anyone acting (ADR-029). Internal supersession has a cause - somebody approved
something - so there is a moment to record and a person to attribute it to, and recording it
costs nothing that inferring it would save.

**3. The supersession is written in the same transaction as the approval that caused it.**
Otherwise the window it exists to close simply moves: two approved versions, between the
approval landing and the supersession landing. Same reasoning as ADR-024 decision 1, and the
same transaction boundary.

**4. Only an approved version is superseded.** A draft, a version already superseded, and a
withdrawn one are not displaced by a new approval - they were not the one in force to begin
with. Nothing is written for them, so the history says only what happened.

**5. Superseding changes nothing about the superseded version except its state.** Its content,
its pinned validating context, its signatures and its history are untouched and remain
retrievable and reconstructable. A superseded version is still the version that was in force
between two dates, and an inspection asks about exactly that.

**6. Withdrawal is a different act and stays manual.** Superseding happens because something
replaced a version; withdrawing happens because somebody decided to stop standing behind it,
possibly with nothing to replace it. Automating the first and not the second is not an
inconsistency - it is the difference between a consequence and a decision.

## Alternatives considered

- **Leave supersession to the caller.** What the model allowed until now. Rejected: the window
  where two versions are both approved is a real ambiguity, and closing it by asking every
  caller to remember is the pattern this codebase has repeatedly replaced with a decorator or a
  transaction.

- **Derive it, as the market side does.** "Superseded" would mean "a later version is approved",
  computed on read. Tempting for symmetry, and it loses the attribution: no actor, no moment, no
  reason. On the market side there is genuinely nobody to attribute the passage of time to; here
  there is.

- **Supersede on approval of the next version only if the caller asks.** A flag on the
  transition. Rejected: it makes the normal case optional, and the abnormal case - approving a
  version while deliberately leaving its predecessor approved - is not a case anyone has
  described.

## Consequences

- The lifecycle store's append takes the consequence alongside the transition and its pin, so
  all three land together. The port grows a parameter rather than a second method, because they
  are one write.
- A label with several previously approved versions - possible only if this rule was ever not
  applied - would have exactly one superseded per approval. The implementation supersedes the
  most recent approved version, and any earlier ones stay as they are; correcting historical
  data is a migration question, not a runtime one.
- The `supersede` action remains in the model and remains available to a caller. Nothing needs
  it now, and removing it would be removing a transition an organisation might legitimately
  configure differently.
