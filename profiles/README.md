# profiles/ - FHIR conformance artifacts

FHIR conformance resources that define the target structure per scheme (capability 10,
Regulatory Mapping & Conformance Profiles):
- `StructureDefinition`s (profiles and extensions),
- `ValueSet` / `CodeSystem` / `ConceptMap` bindings and mappings,
- `ImplementationGuide` packages,
- `StructureMap`s for cross-scheme transforms (e.g. FHIR ePI <-> SPL).

These are consumed by templates (capability 3), validation (11), completeness checks (12), and
migration transforms (4). Baseline standards: HL7 Global Core ePI IG, EMA/EMRN ePI + EU QRD,
FDA SPL, and national extensions.

## Contents

- `packages/` - the **pinned, vendored IG packages** and their digest manifest, so validation
  resolves offline and reproducibly. See [packages/README.md](packages/README.md) and
  [ADR-016](../design/adrs/0016-pinned-epi-ig-release-and-section-codes.md). The conformance
  target today is `hl7.fhir.uv.emedicinal-product-info` 1.0.0 (STU1, FHIR R5).

Hand-authored StructureDefinitions, ValueSets, and StructureMaps live here alongside the
vendored packages as they are written. Markets reference a profile package and version from
`config/`, so adopting a release is configuration rather than a code change.
