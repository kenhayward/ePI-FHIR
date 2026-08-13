# tests/ - Test assets and fixtures

Cross-cutting test material that is not tied to a single service:
- sample FHIR ePI bundles and product-graph resources,
- conformance fixtures (valid and intentionally invalid) for validation (11) and compliance (12),
- SPL and legacy samples for migration (4),
- end-to-end scenario fixtures (author -> validate -> approve -> render -> publish).

Service-level unit and contract tests live with their service under `src/`. Keep regulated-domain
test data synthetic - no real product or personal data.
