# ADR-027: Secondary identifiers, and the platform's own code systems

Status: accepted
Date: 2026-08-14

Settles two questions left open since iteration 1: where a submitter's or legacy identifier
lives given `Bundle.identifier` is 0..1 (PR 5 review notes, ADR-015 consequences), and whether
the tags the platform asserts should become published `CodeSystem` resources (ADR-017
consequences, CAP-SCM-006). Both were scheduled for "when the content model is next opened"
([iteration-3.md](../iteration-3.md) Section 2.3, delivery row 13); reuse has opened it.

## Context

**Two identifiers, one slot.** ADR-015 gives `Bundle.identifier` to the platform's own minted
identity, because identity has to be stable, meaningless and independent of any FHIR server. But
content does not always arrive without an identifier of its own. Migration brings labels
carrying a legacy identifier; an affiliate may submit content under a reference its own systems
use; a regulator's correspondence names a submission. `Bundle.identifier` is 0..1 and taken, so
today those identifiers have nowhere to go, and the platform silently drops them.

Dropping them is worse than untidy. A migrated label whose legacy identifier is lost cannot be
reconciled against the system it came from, which is exactly the check a migration needs to pass
before anyone will trust it.

**Tags asserting code systems.** The platform writes `Coding.system` values of its own -
affiliate, market, template, reusable unit. A `Coding.system` is a claim that a code system
exists at that URI. The platform is therefore asserting code systems it has never defined, and
ADR-017 explicitly left open whether they should be published as `CodeSystem` resources.

## Decision

**1. Secondary identifiers live on `Composition.identifier`, never on `Bundle.identifier`.** The
Bundle's identifier is the platform's, always and only (ADR-015). The anchoring `Composition`
carries what the content arrived with.

This is not a workaround for a cardinality problem, though it does solve one. `Bundle.identifier`
identifies *this document as this platform holds it*; a legacy or submitter identifier identifies
*the thing the content is about, in another system*. They are different assertions, and putting
the second one in the first one's slot is what made it feel like a shortage of space.

**2. A secondary identifier records which system it came from, and is never treated as
identity.** Nothing resolves by it, nothing mints from it, and two documents may carry the same
one - a legacy system that reused an identifier is a fact to record, not an error to reject. It
is evidence about provenance, and the platform's own identifier remains the only thing that
identifies anything.

**3. The platform does not publish `CodeSystem` resources for its own tags.** They are
identifier namespaces the deployment owns (ADR-017), and their codes are values the deployment
already governs elsewhere: markets in `config/markets`, affiliates in the identity provider,
templates and units as content with their own lifecycles. Publishing a `CodeSystem` would create
a *second* place those values are defined, and the second place is the one that goes stale.

**4. Where a value set is genuinely needed, it is generated from the governing source, never
hand-maintained.** If terminology binding (capability 6) or an external consumer needs the
markets as a `ValueSet`, it is derived from `config/markets` at build or publish time. A
generated artefact that disagrees with its source is a build failure; a hand-written one is a
discrepancy nobody notices.

**5. This is revisited if the platform ever publishes its content outside itself.** A downstream
consumer given a `Coding.system` it cannot resolve is a real problem, and publishing terminology
is the answer to it. That is capability 14's problem, and the decision to take then is which
subset to publish - not whether to maintain two definitions internally now.

## Alternatives considered

- **A second `Bundle.identifier`... which does not exist.** `Bundle.identifier` is 0..1 in R5.
  Named here because it is the first thing anyone checks.

- **An extension on the Bundle for secondary identifiers.** Would work, and would put provenance
  about the content on the container rather than on the content. The Composition is the thing the
  identifier is about.

- **`Provenance` alone.** ADR-015 already says migration records legacy identifiers "plus
  `Provenance` to the source artefact", and `Provenance` is where the *act* of migration belongs -
  who converted what, when, from which artefact. It is the wrong place to look up "what was this
  called before", because it describes an event rather than the label. Both, then: the identifier
  on the Composition, the act in `Provenance` when migration arrives.

- **Publishing `CodeSystem` resources for the platform's tags.** The FHIR-complete answer, and
  what a terminology purist would expect. Rejected for now under decision 3: it duplicates
  definitions that already have a governing source, and duplication in a regulated system is not
  a tidiness problem but a divergence waiting to be discovered during an inspection.

## Consequences

- Content may carry secondary identifiers, and the platform preserves them across versions
  without interpreting them. Round-trip fidelity (CAP-SCM-010) already requires that anything
  the platform does not understand survives; this makes it explicit for a field it does
  understand and deliberately does not act on.
- Migration (capability 4) has the slot it needs, and reconciliation against a legacy system is
  possible without inventing one later.
- Search by secondary identifier is not provided. It is the obvious next request from anyone
  running a migration, and it is a search-projection change rather than a model one - recorded
  rather than built.
- The platform's `Coding.system` URIs remain unresolvable to an outside consumer. That is
  acceptable while the platform is the only consumer, and decision 5 names when it stops being.
