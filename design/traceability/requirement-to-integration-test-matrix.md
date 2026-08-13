# Requirement to Integration Test Matrix

GENERATED FILE - do not edit by hand. Regenerate with
`python tools/build-traceability.py`; CI fails if it is out of date.

The **ascent of the V**: integration tests validate capability requirements end to end,
against real collaborators rather than mocks. Intent is declared in
`integration-tests.json`; the implementing test is discovered from the code by its IT id.

Name integration tests for the case they validate, for example
`IT_001_bundle_round_trips_through_create_and_read`.

## Coverage

- Integration tests declared: **9**
- Implemented in code: **5**
- Requirements validated by at least one integration test: **15**
- Scheduled requirements still without one: **0**

## Integration tests

| Test | Scenario | Verifies | Iteration | Status | Implementation |
|---|---|---|---|---|---|
| IT-001 | A conformant ePI Bundle round-trips through create and read with no content loss | CAP-SCM-010, CAP-SCM-001 | 1 | implemented | `IT_001_a_conformant_bundle_round_trips_through_create_and_read_without_content_loss` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| IT-002 | A caller outside the resource affiliate or market is denied and sees nothing | CAP-IAM-002, CAP-IAM-007 | 1 | planned | - |
| IT-003 | A content write and an access decision each produce an immutable audit record | CAP-AUD-001, CAP-AUD-002, CAP-IAM-009 | 1 | planned | - |
| IT-004 | A second market is served by adding configuration alone, with no code change | CAP-CFG-004, CAP-CFG-001 | 1 | implemented | `IT_004_a_new_market_is_added_by_configuration_alone` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs)<br>`IT_004_the_shipped_market_configuration_loads_and_validates` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |
| IT-005 | Malformed content is rejected with itemised located errors and leaves no partial state | CAP-VAL-003, CAP-VAL-005 | 1 | implemented | `IT_005_a_rejected_new_version_leaves_the_previous_version_intact` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_malformed_content_is_rejected_with_itemised_located_errors` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_rejected_content_leaves_no_partial_state` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_valid_content_passes_the_gate_and_is_stored` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs) |
| IT-006 | An attempt to mutate an existing version is rejected and history remains reconstructable | CAP-SCM-007 | 1 | implemented | `IT_006_an_attempt_to_mutate_an_existing_version_is_rejected_and_history_is_reconstructable` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| IT-007 | A request without a valid OIDC token is rejected before reaching content | CAP-IAM-001 | 1 | planned | - |
| IT-008 | Creating a document emits a content event to the backbone | CAP-EVT-001 | 1 | planned | - |
| IT-009 | An invalid market definition is rejected before activation | CAP-CFG-006 | 1 | implemented | `IT_009_an_invalid_market_definition_is_rejected_before_activation` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |

## By requirement

| Requirement | Integration tests | Requirement text |
|---|---|---|
| CAP-AUD-001 | IT-003 | Capture a comprehensive audit trail of all GxP-relevant actions (who, what, when, why, before/after values). |
| CAP-AUD-002 | IT-003 | Store audit records tamper-evidently/immutably (append-only). |
| CAP-CFG-001 | IT-004 | Externalise configuration for markets/regulators, lifecycle state models, workflows, validation/compliance rules, terminology bindings, publishing routing, and event schemas. |
| CAP-CFG-004 | IT-004 | Onboard a new market/regulator via configuration without a code release. |
| CAP-CFG-006 | IT-009 | Validate configuration consistency before activation. |
| CAP-EVT-001 | IT-008 | Provide a publish/subscribe event backbone for inter-capability communication. |
| CAP-IAM-001 | IT-007 | Authenticate via the enterprise IdP (OIDC/SAML) with SSO/federation and MFA (delegated to IdP). |
| CAP-IAM-002 | IT-002 | Enforce combined RBAC + ABAC authorization on every action and API. |
| CAP-IAM-007 | IT-002 | Enforce multi-tenant isolation of affiliate data. |
| CAP-IAM-009 | IT-003 | Record all access-control decisions and administration changes to audit (#19). |
| CAP-SCM-001 | IT-001 | Represent an ePI as a FHIR document `Bundle` anchored by a `Composition` with typed, coded sections. |
| CAP-SCM-007 | IT-006 | Define **canonical identifier and versioning semantics** for documents, sections, and reusable units (stable IDs across languages/markets/versions). |
| CAP-SCM-010 | IT-001 | Preserve full fidelity round-trip: a conformant ePI can be represented and re-serialised without content loss. |
| CAP-VAL-003 | IT-005 | Validate structural well-formedness and reference integrity (#2), including no dangling references in approval candidates. |
| CAP-VAL-005 | IT-005 | Produce structured, actionable issues with severity and precise location (section/element). |
