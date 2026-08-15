# ADR-029: Effective dating, per market

Status: accepted
Date: 2026-08-15

Realises CAP-LCM-004 and the "in force" half of CAP-SCH-002. Required by iteration 3
([iteration-3.md](../iteration-3.md) acceptance criterion 4, delivery row 5), and carried from
iteration 2, which planned it and did not build it.

## Context

Approval and effect are not the same event. A regulator approves a version in March and it takes
effect in June; a safety change is approved and effective the same afternoon; a national scheme
requires a notice period. Until now the platform could say which version a market has approved
and could not say **which version is in force today**, which is the question publishing asks,
the question a call centre asks, and the question an inspector asks about a date in the past.

Two things have to be settled: where the date lives, and whether "in force" is stored or worked
out.

The first is straightforward once stated plainly. A version approved in Great Britain and in the
European Union has two approvals, and nothing says they take effect on the same day. An
effective date on the version would be a single answer to a question that has one answer per
market - exactly the conflation ADR-005 separated internal from per-market state to avoid.

The second is the one that goes wrong quietly. A stored "in force" flag is correct at the moment
it is written and wrong from the first midnight afterwards, and nothing in the system notices,
because no write happened. The failure needs a clock to occur and there is no clock in the
write path.

## Decision

**1. An effective date belongs to a market approval, not to a version.** It is recorded on the
transition that records the regulator's decision, alongside who recorded it and when. A version
carries no effective date of its own, because it does not have one.

**2. "In force" is derived at the moment of asking, never stored.** In force in a market at an
instant means: the latest version whose approval in that market took effect at or before that
instant, and whose approval has not since been withdrawn. It is a query over the append-only
transition history, so it is answerable for any past instant and cannot be stale.

The same reasoning as ADR-019 decision 4, and worth repeating because this is the case where
storing it is most tempting: it is a flag everyone wants to index on.

**3. Recording an approval must state when it takes effect.** There is no default, and
"immediately" is stated by giving the approval's own timestamp rather than by omitting the
field. A missing date defaulted to now is a guess that reads as a fact, and the difference is
invisible afterwards.

**4. An effective date may be in the future, and the platform does not act on the arrival of
one.** A version approved today and effective next month is approved, not in force, and becomes
in force without anything happening: no job, no event, no state change. Publishing (capability
14) asks what is in force at the moment it publishes, and gets the right answer without anyone
having scheduled it.

**5. An effective date may not precede the approval it belongs to.** A market cannot bring a
version into force before it decided to. This is refused rather than tolerated, because the
alternative is a history in which effect precedes cause and every "in force at" answer computed
from it is wrong in a way no later check can detect.

**6. Withdrawal ends effect from the moment it is recorded.** A withdrawn approval does not
delete anything: the version, its approval and its effective date all remain, and "in force on
the third of March" still answers correctly for a date before the withdrawal. What changes is
only what is in force now.

**7. Nothing about effect changes internal lifecycle state.** A version that is approved
internally and in force in Great Britain is one fact about the organisation and one about a
market (ADR-005). Effective dating lives entirely on the market side.

## Alternatives considered

- **An effective date on the version.** One field, easy to index, and it cannot express the
  normal case: the same content in force on different days in different markets. Rejected for
  the reason ADR-005 exists.

- **A stored "in force" flag, maintained by a scheduled job.** Fast to read and wrong between
  midnight and whenever the job runs, with no way to tell the difference. It also makes the
  correctness of a regulatory answer depend on a background process having succeeded, which is
  the sort of dependency nobody remembers until it has failed for a week.

- **Effective date as a property of publication rather than approval.** Defensible: what is "in
  force" is arguably what has been published. Rejected because it makes the answer depend on a
  downstream capability that does not exist yet, and because a market's approval taking effect
  is a regulatory fact whether or not this platform published anything.

- **Allowing a backdated effective date, with a warning.** Genuinely tempting, because
  data migration will produce approvals whose effect predates their record in this system.
  Rejected here and revisited with migration (capability 4): a migrated approval carries its
  *original* approval date too, so the constraint holds against the real dates rather than
  against the date the row was written.

## Consequences

- `MarketStateTransition` gains an effective date, populated on transitions that record an
  approval and absent on the others. The store, the conformance suite and the schema follow.
- "Which version is in force" is a scan of a version's market history today. It is the obvious
  thing to project into the search index, and doing so is a projection change rather than a
  model one - recorded rather than built.
- The current-approved query (ADR-022 decision 7) answers a different question from the in-force
  query, and both are worth having: one is "what has this market agreed to", the other is "what
  applies today". They differ exactly during a notice period.
- Supersession follows from this rather than being recorded separately for market purposes: a
  version is superseded in a market when a later one takes effect there. The internal
  `superseded` state remains an act the organisation records about itself.
