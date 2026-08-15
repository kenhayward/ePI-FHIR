# ADR-031: Workflow routing

Status: accepted
Date: 2026-08-15

Realises CAP-WFL-001 and supports CAP-WFL-002. Carried from iteration 2, which planned it and
did not build it ([iteration-3.md](../iteration-3.md) Section 2.2, delivery row 7).

## Context

Iteration 2 built the approval *gate*: a version may not move to approved unless the model
permits it, the actor is not the author, and a valid signature covers the exact bytes. What it
did not build is anything that **asks someone to act**. A version submitted for review sits
there, and the only way anyone learns of it is by searching for it.

That is the difference between a control and a process. The gate says who may approve; routing
says who has been asked to, when, and whether they still have not.

The risk in building it is subtle and worth naming first. A task list is a second place where
the state of a version appears to live, and the moment anything reads a task to decide whether
a version may move, there are two authorities and they will disagree. Every workflow engine that
has ever been bolted onto a domain model has this problem.

## Decision

**1. A task records that somebody was asked to act. It is not state, and nothing consults it to
decide whether a transition may happen.** The state model remains the only authority on what may
move and by whom (ADR-019). A task can be missing, stale or wrong and the gate still holds.

**2. Tasks are raised by transitions and closed by transitions.** Submitting for review raises a
review task; approving closes it. Nothing marks a task done by hand, because "done" means the
thing was actually done, and the only evidence of that is the transition.

A task belongs to the state that raised it, so **any** transition out of that state closes it,
not only the action the task asked for. A reviewer who returns a translation rather than
approving it has answered the ask; leaving the task open would keep it on their list after the
version had gone back to its author. (Recorded here because the first implementation matched on
the action and got this wrong, which the variant review process found immediately.)

**3. Routing is configuration, per label type and market.** Which state raises a task, what the
task asks for, and who it goes to, are data (capability 21, ADR-012). An organisation whose
review process differs is a configuration change, and a market with an extra step is a row in a
file rather than a branch in code.

**4. Assignment is to a role, and a person claims it.** Direct assignment to a person is
possible and is the exception: a task assigned to somebody on leave is a task nobody sees, and
the failure looks like nothing happening. A role that nobody holds is at least a configuration
error somebody can find.

**5. Reassignment is recorded, never overwritten.** Who a task was assigned to and when it moved
is part of the record of how a version came to be approved. Append-only, like everything else
that is evidence.

**6. Escalation is derived, not scheduled.** A task overdue by the configured period is overdue
whether or not anything noticed - the same reasoning as ADR-029: nothing fires on a clock,
because a job that has failed for a week is indistinguishable from a queue that is empty. What
is overdue is a query, and whoever asks gets the current answer.

**7. Completing a task does not perform the transition.** A caller performs the transition
through the engine, which applies segregation of duties and the signature gate as it always
does, and the task closes because the transition happened. There is deliberately no route by
which acting on a task can move a version.

## Alternatives considered

- **A workflow engine (Camunda, Elsa, Temporal).** Real capability, and D3's stack table already
  names Camunda for DMN. Rejected for this: a BPMN engine owns process state, which makes it the
  second authority decision 1 exists to prevent, and the ninety percent of it we would not use
  is still there to be configured wrongly. Revisit if routing grows conditional branches,
  parallel gateways and timers - none of which the current requirement asks for.

- **Tasks as lifecycle states.** "In review with Ben" as a state. Rejected: it multiplies the
  state model by the number of people, and the state model is a control that has to stay small
  enough to read.

- **Assignment to a person by default.** Simpler to explain and what most systems do. Rejected
  under decision 4: it makes a person's absence a silent stall.

- **A scheduled escalation job.** Familiar and operationally visible. Rejected under decision 6,
  and for the reason ADR-029 gives: correctness that depends on a background process having run
  is correctness nobody can check by looking.

## Consequences

- Tasks are a governance record and are held in the governance store, append-only, migrated like
  the rest (ADR-024). A task's current assignment and state are derived from its events, not
  stored as fields.
- Raising and closing happen inside the transition that causes them, in the same transaction, for
  the reason ADR-030 gives about supersession: otherwise the window in which the two disagree
  simply moves.
- Notification - telling somebody a task exists - is capability 20's, not this one's. Routing
  records the ask; the event backbone carries it. That seam exists already and is not wired here.
- "What is waiting for me" is a query over open tasks filtered by the caller's roles, and it is
  permission-scoped like everything else (ADR-022). It is not built in this pull request.
