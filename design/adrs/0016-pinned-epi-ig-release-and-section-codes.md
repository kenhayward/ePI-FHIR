# ADR-016: Pinned ePI IG release and section code systems

Status: proposed
Date: 2026-08-13

Resolves D3 Section 15 open item 3 and D2.1 open item 3. Realises the conformance target
CAP-VAL-001 validates against, and constrains CAP-TPL-003, CAP-SCM-008, CAP-TRM-009.
Required by iteration 1 (`design/iteration-1.md` Section 9).

## Context

Validation has no yardstick until the conformance target is fixed. CAP-VAL-001 requires
validation "against the applicable active profile(s)", and CAP-VAL-009 requires validation to
use "the profile version active for the content's market and date" - both presuppose a pinned,
versioned profile set.

Two properties matter more than which release is chosen:

- **Determinism.** GxP/CSV controlled release (D3 Section 10.3) means a build validates the same
  content the same way today and in five years. A validator that resolves profiles or value sets
  over the network at runtime is not reproducible, and a regulator asking why a 2027 label passed
  validation deserves a better answer than "the registry served something different then".
- **Historical reproducibility.** CAP-LCM-006 requires any historical version to be
  reconstructable. Content approved under one profile release may not pass a later one; that is
  normal and expected as standards evolve. What must never happen is a later profile release
  silently invalidating, or silently re-validating, previously approved content.

The standards themselves are in flux - D1 Section 10 records "standards flux" as a live risk,
mitigated by config-as-data and profile versioning.

## Decision

**1. FHIR R5 is the baseline wire format and validation target.** This matches the dev stack
(`HAPI_FHIR_FHIR_VERSION: R5`) and D3 Section 12. D1 Section 8.2 admits R4 or R5; this ADR fixes
R5.

**2. The conformance target is a pinned, published release of the HL7 Global Core ePI IG.**
D1 Section 8.2 records STU1, with v1.1.0 in build. **Only published releases are used at a
validation gate; build or CI snapshots never are.** The exact package identifier and version
string are configuration data, not code, and are confirmed against the published package
registry before the validation gate is built (PR 5 of iteration 1).

**3. IG packages are vendored under `profiles/` and resolved offline.** The validator never
fetches a profile, value set, or package over the network at runtime. A package enters the
repository through a reviewed pull request like any other dependency.

**4. Section codes are bound through the pinned IG's value sets**, not enumerated in code. The
ePI and QRD section taxonomy draws on LOINC, EDQM standard terms, and EU section codes
(D2.1 Section 2.4); which system applies to which element is the IG's business, and the platform
resolves it from the pinned package. Terminology releases are pinned alongside the IG, and code
validation uses the version effective for the content's date (CAP-TRM-009).

**5. Every validation result records the profile package version that produced it, and every
approved version records the profile version it was validated against** (ADR-015 decision 8).
This is what makes a historical validation verdict reproducible rather than merely repeatable.

**6. Upgrading the pinned release is a governed configuration change** (capability 21):
effective-dated, approved, and audited. It is **never retroactive**. Already-approved content
keeps the profile version it was approved under; it is re-validated against the new release only
when it is next revised, or deliberately as a migration exercise. The regulatory sandbox
(D3 Section 10.2) exists for exactly this rehearsal.

**7. Per-market profiles layer over the core** rather than replacing it (capability 10). A market
configuration names its own profile package and version, defaulting to the core pin. This is what
lets a market adopt a new EU release on its own timetable without a platform release.

## Alternatives considered

- **Track the IG's latest release automatically.** Cheap to operate and always current.
  Rejected: it makes validation non-deterministic and lets an upstream publication change a GxP
  verdict with no change on our side and no audit record. It would also break CAP-VAL-009, which
  is explicit that validation uses the version effective for the content's date.
- **Resolve profiles and terminology from a network registry at runtime.** Rejected for the same
  determinism reason, plus it makes the validator's behaviour depend on network availability at
  a lifecycle gate.
- **Adopt the in-build v1.1.0 now** rather than the published STU release. Tempting, because it
  is closer to where the standard is going. Rejected for validation gates: an in-build IG can
  change under us without a version bump, which is precisely what decision 2 exists to prevent.
  The regulatory sandbox is where in-build releases are exercised.
- **Enumerate section codes in application code**, avoiding a dependency on the package. Rejected:
  it forks the taxonomy, and adding a market or adopting a release would become a code release,
  contradicting ADR-012 and capability 21.
- **FHIR R4 as the baseline.** Rejected here because the platform and dev stack are already R5,
  but see the risk below: the EU profile set is the constraint that could reopen this.

## Consequences

- Iteration 1's validation (FN-VAL-001) validates against the pinned package, so PR 5 cannot
  start until the exact version string is confirmed. PR 4 (content core) is unaffected.
- `profiles/` gains vendored IG packages, and `config/` gains the market-to-profile-version
  binding. Both are reviewed artefacts under the same controls as code.
- A profile upgrade becomes a deliberate, testable event with a blast radius that can be
  measured before adoption, rather than a silent drift.
- Validation is reproducible offline and in a qualified environment, which is what CSV requires.
- The repository carries the licence terms of any vendored package. IG packages are HL7
  material under HL7's terms; this is a content licence, not a software licence, and is
  distinct from the Apache-2.0 rule for dependencies (see `CLAUDE.md`).

## Risks and open points

- **The EU target may not be R5.** The EMA/EMRN ePI profile set and the EU QRD templates are the
  P2 conformance target (capability 10), and if they target a different FHIR release the
  mapping cost lands there, not here. Mitigation: profiles are config-as-data, so the platform
  binds a market to a profile package rather than compiling a version in; and the canonical
  content model (capability 2) is deliberately expressed in terms the IG defines rather than in
  release-specific extensions. **Action: confirm the FHIR release each target scheme requires -
  Global Core, EMA/EMRN EU, and any national profiles - before capability 10 work begins in P2.**
- **The exact package identifier and version string are not yet recorded.** Deliberate: naming a
  version that is not verified against the published registry would be worse than naming none.
  Blocking PR 5 of iteration 1, not PR 4.
