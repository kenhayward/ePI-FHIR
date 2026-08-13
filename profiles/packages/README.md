# profiles/packages/ - Vendored FHIR conformance packages

The pinned conformance target, held in the repository so validation resolves **offline** and a
verdict is reproducible years later ([ADR-016](../../design/adrs/0016-pinned-epi-ig-release-and-section-codes.md)).

## What is pinned

| Package | Version | FHIR | Licence | Why |
|---|---|---|---|---|
| `hl7.fhir.uv.emedicinal-product-info` | 1.0.0 | 5.0.0 | CC0-1.0 | The Global Core ePI IG: the conformance target |
| `hl7.terminology.r5` | 5.0.0 | 5.0.0 | CC0-1.0 | Required by the ePI IG |
| `hl7.fhir.uv.extensions.r5` | 1.0.0 | 5.0.0 | CC0-1.0 | Required by the ePI IG |

`1.0.0` is the **only published release** of the ePI IG. Its history holds a ballot
(`1.0.0-ballot`), this trial-use release, and a continuous build. ADR-016 decision 2 excludes
ballot and build releases from validation gates.

`hl7.fhir.r5.core` 5.0.0 is a dependency of the two supporting packages but is **not** vendored
here: the FHIR R5 core definitions arrive with the validator, in the
`Hl7.Fhir.Specification.Data.R5` NuGet package. See `manifest.json` and ADR-016 for the
reasoning. `tools/verify-profile-packages.py` knows about this single exception, so no other
dependency can go missing unnoticed.

## Integrity

`manifest.json` records the SHA-256, size, licence, FHIR version, dependencies, and source URL
of every package. CI verifies on every pull request:

```bash
python tools/verify-profile-packages.py
```

It checks that each file is present, that its digest matches, that the package's own manifest
agrees with what we recorded about it, and that the dependency closure is complete. A pinned
package is only a pin if the bytes are the ones that were reviewed.

## Changing a pin

Adopting a new release is a governed configuration change, never an in-place update
(ADR-016 decision 6):

1. Download the package from the registry and confirm it is a **published** release, not a
   ballot or continuous build.
2. Read the manifest **inside the tarball** for its version, `fhirVersions`, and dependencies.
   Do not trust the registry's summary - it under-reported the ePI package's dependencies, and
   a missing dependency means validation silently reaches for the network.
3. Add the new package alongside the old one rather than replacing it. Content approved under
   the old release keeps being validated against the old release.
4. Regenerate `manifest.json` digests and run the verification script.
5. Rehearse in the regulatory sandbox (D3 Section 10.2) before any market binds to it.
