# profiles/ - FHIR conformance artifacts

FHIR conformance resources that define the target structure per scheme (capability 10,
Regulatory Mapping & Conformance Profiles):
- `StructureDefinition`s (profiles and extensions),
- `ValueSet` / `CodeSystem` / `ConceptMap` bindings and mappings,
- `ImplementationGuide` packages,
- `StructureMap`s for cross-scheme transforms (e.g. FHIR ePI <-> SPL).

These are consumed by templates (capability 3), validation (11), completeness checks (12), and
migration transforms (4). Baseline standards: HL7 Global Core ePI IG, EMA/EMRN ePI + EU QRD,
FDA SPL, and national extensions. Pin exact IG/profile releases here and reference them from
`config/`.
