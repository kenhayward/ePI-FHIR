# ADR-016: Pinned ePI IG release and section code systems

Status: accepted
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
**Only published releases are used at a validation gate; build or CI snapshots never are.**
Verified against the registry on 2026-08-13, the pin is:

| Field | Value |
|---|---|
| Package | `hl7.fhir.uv.emedicinal-product-info` |
| Version | `1.0.0` |
| Status | trial-use, sequence STU1, published 2023-07-26 |
| FHIR version | `5.0.0` |
| Canonical | `http://hl7.org/fhir/uv/emedicinal-product-info` |
| Licence | CC0-1.0 |

The IG's publication history contains exactly three entries: `1.0.0-ballot` (ballot,
2022-12-04), `1.0.0` (trial-use, current), and `current` (ci-build). **`1.0.0` is therefore the
only published release**, and the v1.1.0 that D1 Section 8.2 mentions exists only as a
continuous build, which decision 2 excludes from validation gates.

**3. IG packages are vendored under `profiles/packages/` and resolved offline.** The validator
never fetches a profile, value set, or package over the network at runtime. A package enters
the repository through a reviewed pull request like any other dependency, and
`profiles/packages/manifest.json` records the SHA-256 of each, verified in CI by
`tools/verify-profile-packages.py`. A pin is only a pin if the bytes are the ones that were
reviewed.

**The whole dependency closure is vendored, with one deliberate exception.** The ePI package
depends on `hl7.terminology.r5` 5.0.0 and `hl7.fhir.uv.extensions.r5` 1.0.0, and both of those
depend on `hl7.fhir.r5.core` 5.0.0. The three IG-level packages are vendored (8.2 MB together).
`hl7.fhir.r5.core` is **not**: the FHIR R5 core definitions arrive with the validator itself, as
the `Hl7.Fhir.Specification.Data.R5` NuGet package that carries `specification.zip` (verified,
10 MB). Vendoring a second 17 MB copy would duplicate it and, worse, risk the vendored core and
the SDK's own core disagreeing about the same FHIR release. NuGet restore is already part of the
deterministic build with a pinned version, so this costs nothing in reproducibility. The
verification script enforces the closure and knows about this single exception, so no *other*
package can go missing unnoticed. **If the .NET validator is ever replaced by tooling that does
not carry core definitions, `hl7.fhir.r5.core` must be vendored at that point.**

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
- **FHIR R4 as the baseline.** Rejected: the platform and dev stack are already R5, and
  verification has since confirmed that both the Global Core ePI IG (`fhirVersions: ["5.0.0"]`)
  and the EMRN EU ePI IG are R5, so nothing in the target set argues for R4.
- **Vendoring `hl7.fhir.r5.core` alongside the IG packages**, for a single mechanism and a
  repository that is self-sufficient with no NuGet at all. Rejected: 17 MB duplicating the
  10 MB `specification.zip` the validator already carries, and two copies of the same core
  release can disagree with the SDK version in use. The closure check in
  `tools/verify-profile-packages.py` records the exception explicitly so it cannot be forgotten.

## Consequences

- Iteration 1's validation (FN-VAL-001) validates against the pinned package. **The version is
  now confirmed and the packages are vendored, so PR 5 is unblocked.**
- `profiles/packages/` carries the vendored packages and their digest manifest. `config/` gains
  the market-to-profile-version binding in PR 5, where FN-CFG-002 resolves it. Both are reviewed
  artefacts under the same controls as code.
- A profile upgrade becomes a deliberate, testable event with a blast radius that can be
  measured before adoption, rather than a silent drift.
- Validation is reproducible offline and in a qualified environment, which is what CSV requires.
- The repository carries the licence terms of any vendored package. **All three vendored
  packages are CC0-1.0**, a public-domain dedication, so vendoring them raises no distribution
  question at all. This is better than this ADR originally assumed: it was drafted expecting
  HL7 terms requiring care. Licence remains a per-package check at pin time, and a future
  package under different terms would need that check again. Note this is a *content* licence
  question, distinct from the Apache-2.0 rule for software dependencies (see `CLAUDE.md`).

## Risks and open points

- **The EU target being R4 - now closed.** Verified 2026-08-13: the EMRN ePI IG is built on
  FHIR 5.0.0, as is the Global Core ePI IG. R5 holds across both targets and no R4 mapping cost
  lands in capability 10. National profiles still need the same check as they are adopted.
- **The EU IG is a draft preview and is not in the public package registry.** The EMRN ePI IG
  1.0.0 describes itself as "provided for preview purposes only", its canonical is
  `http://ema.europa.eu/fhir/ImplementationGuide/EUePI`, and its package identifier is `EUePI`,
  which is not the `hl7.eu.fhir.*` convention and returns 404 from `packages.fhir.org` - it is
  distributed only from EMA's own site. Two consequences for P2: EU profiles cannot be obtained
  through ordinary package tooling and will need a deliberate acquisition and vendoring step,
  and under decision 2 a preview release is **not** a validation-gate target. Neither blocks
  P0 or P1. **Action: track the EMRN IG to its first non-preview release before capability 10
  work begins in P2.**
- **Registry metadata under-reports dependencies.** The registry's summary for the ePI package
  listed no dependencies; the package manifest inside the tarball declares two, each with a
  further dependency. Anything that pins a package must read the manifest in the artefact
  rather than trusting the registry's summary, or offline resolution fails at the first run.
