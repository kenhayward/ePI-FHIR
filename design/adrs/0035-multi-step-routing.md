# ADR-035: Multi-step routing, per market and label type

Status: accepted
Date: 2026-08-16

Realises CAP-WFL-001 and CAP-WFL-006, and depends on CAP-WFL-005. Extends
[ADR-031](0031-workflow-routing.md), which established what a task is and deliberately shipped
one rule per state. Required by iteration 3 ([iteration-3.md](../iteration-3.md) delivery row
7c), where it is recorded as an open debt against ADR-031.

## Context

ADR-031 gave the platform routing: a state raises an ask, of a role, with a due period, from
configuration. It shipped with two limits, both recorded at the time:

- **One rule per state.** A state could ask one thing of one role. CAP-WFL-001 asks for
  *multi-step* review.
- **One model.** Nothing selected a different process for a different market or label type,
  which is the other half of the same requirement.

Neither is hard to lift. What needs deciding first is what "multi-step" means here, because two
readings of it lead to very different systems and only one of them is consistent with what is
already built.

**Reading A: a step is a state.** Review passes through medical review, then legal review, then
approval, and each of those is a state a version holds. The move between them is a lifecycle
transition, which is already gated, signed where the model says so, segregated where the model
says so, and recorded append-only as evidence.

**Reading B: a step is a sub-task inside a state.** A version sits in `in-review` while three
sign-offs are collected, and the transition out is refused until all three are in.

Reading B is superficially attractive and it breaks two things at once. ADR-031 decision 2 says
tasks are raised by transitions and closed by transitions, because "done" means the thing was
actually done and the only evidence of that is the transition; sub-task sign-off needs some
other act to close a step. And `IWorkflowStore` states plainly that nothing in it is consulted
to decide whether a transition may happen - so that a task may be missing, stale or wrong and
the gate still holds. Reading B makes the task store an authority on transitions, which means a
routing bug can now block an approval indefinitely or, worse, let one through.

CAP-WFL-005 settles it independently: "**drive lifecycle transitions in #7 on step
completion**". A step completing causes a transition. A step is a state.

## Decision

**1. Sequential steps are states; parallel asks are rules.** A review path with three steps is
three states in the lifecycle model, and the moves between them are transitions with everything
a transition already carries. Within one state, several people may be asked at once - that is
several routing rules for one state, all raised together.

This is what lifts the one-rule-per-state limit, and it lifts it without making the task store
an authority on anything. ADR-031 decision 1 stands unchanged: a task records that somebody was
asked, and never decides whether a transition may happen.

**2. A routing model declares what it applies to, and the catalogue selects.** Each model
carries an `appliesTo` naming a label type, a market, both, or neither. Selection is by
specificity: both beats label type, which beats market, which beats the model that names
neither and is therefore the default.

Label type before market, because a process is more often shaped by what the document is - a
package leaflet is reviewed differently from a summary of product characteristics, everywhere -
than by where it is going. Where that is wrong for an organisation, naming both is exact and
costs one more file.

**3. Ambiguity is refused when the catalogue is loaded, not resolved when it is read.** Two
models claiming the same applicability is a configuration error, and the alternative is a
process that depends on the order files happen to be read in. A directory with no default model
is also refused: a label type nobody wrote a model for would otherwise be routed to nobody, and
a review nobody was asked for looks exactly like a review everybody passed.

**4. The routing subject is read from the content, not supplied by the caller.** The label type
and market that select a model come from the document, the same way its authorisation scope
does (ADR-022). A caller that could state its own label type could choose its own reviewers.

**5. Routing describes the process; the state model enforces it.** A market whose routing model
asks for two review steps and a state model that permits going straight to approval can still
go straight to approval - routing was not asked, and no task was raised. That is the intended
consequence of decision 1 rather than a gap in it: routing is who to ask, and what may happen
is the state model's alone. An organisation that must not allow the short path expresses that
in the state model, where every other such rule lives.

**6. Configuration moves to a directory per process.** Label routing models live under
`config/workflow/label/` and variant routing models under `config/workflow/variant/`. They were
two files loaded by two paths; they are now two catalogues loaded from two directories, and
adding a market's process is adding a file.

## Consequences

Decision 5 is the one to be honest about. This delivers *configurable* multi-step review per
market and label type, which is what CAP-WFL-001 asks for, and it does not deliver *enforced*
step order beyond what the state model already enforces. The two are different claims and the
first is easy to mistake for the second. Where a regulator would expect an enforced sequence,
the states have to exist in the state model, and the platform supports exactly one internal
state model today - so a market needing a genuinely different enforced path is not yet
expressible. Recorded as a debt rather than solved here, because solving it means per-label-type
state models, which is a larger decision than routing.

Conditional routing - CAP-WFL-006's third clause, where the path depends on the nature of the
change rather than on the market - is not delivered and is not attempted. It needs change
classification (capability 8), which does not exist.

Escalation on a due date remains what ADR-031 decision 6 made it: derived when asked, not
scheduled. Several parallel asks in one state means several due dates, and the earliest of them
is not more overdue than the others - each ask is overdue on its own terms.

Nothing tells anyone a task exists. That was already true, and multiplying the asks multiplies
it: a state that asks three people is three people who are not notified.
