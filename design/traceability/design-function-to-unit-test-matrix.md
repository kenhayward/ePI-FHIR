# Design Function to Unit Test Matrix

GENERATED FILE - do not edit by hand. Regenerate with
`python tools/build-traceability.py`; CI fails if it is out of date.

The **descent and the base of the V**: each design function decomposes one or more
capability requirements, and is verified by the unit tests that name it. Functions and
the requirements they satisfy are declared in `design-functions.json`; unit tests are
discovered from the code, so a function shows as unverified until a test names it.

Name unit tests for the function they verify, for example
`FN_CC_006_serialises_without_content_loss`.

## Coverage

- Design functions declared: **23**
- Verified by at least one unit test: **12**
- Not yet verified: **11**

## Matrix

| Function | Name | Component | Satisfies | Iteration | Status | Unit tests |
|---|---|---|---|---|---|---|
| FN-AUD-001 | Compose an audit record with actor, action, target, before and after values | Audit & e-Signature | CAP-AUD-001 | 1 | planned | - |
| FN-AUD-002 | Append an audit record to the append-only store | Audit & e-Signature | CAP-AUD-002 | 1 | planned | - |
| FN-AUD-003 | Reject update or delete of an existing audit record | Audit & e-Signature | CAP-AUD-002 | 1 | planned | - |
| FN-AUD-004 | Record every access-control decision to the audit sink | Audit & e-Signature | CAP-IAM-009 | 1 | planned | - |
| FN-CC-001 | Parse an ePI document Bundle anchored by a Composition | Content Core (FHIR) | CAP-SCM-001 | 1 | verified | `FN_CC_001_reads_a_document_bundle_anchored_by_a_composition` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_a_bundle_that_is_not_of_type_document` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_a_bundle_with_no_entries` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_a_document_bundle_whose_first_entry_is_not_a_composition` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_content_carrying_elements_that_are_not_in_the_model` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_content_that_is_not_a_bundle` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_malformed_json_without_leaking_a_parser_stack_trace` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs) |
| FN-CC-002 | Assign a canonical identifier to a document | Content Core (FHIR) | CAP-SCM-007 | 1 | verified | `FN_CC_002_assigns_a_canonical_identifier_the_caller_did_not_supply` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_002_encodes_no_business_meaning_in_the_identifier` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_002_mints_a_distinct_identifier_for_every_document` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| FN-CC-003 | Create an immutable version snapshot and record its lineage | Content Core (FHIR) | CAP-SCM-007 | 1 | verified | `FN_CC_003_records_the_identifier_on_the_stored_bundle` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_003_rejects_a_new_version_of_a_document_that_does_not_exist` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_003_starts_at_version_one_and_increments_monotonically` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| FN-CC-004 | Persist canonical content through the FHIR REST API | Content Core (FHIR) | CAP-SCM-001 | 1 | verified | `FN_CC_004_content_persists_on_the_server_and_is_readable_by_a_new_client` (src/Epi.ContentCore.IntegrationTests/FhirPersistenceTests.cs)<br>`FN_CC_004_the_server_assigns_its_own_identifiers_which_we_do_not_use_as_identity` (src/Epi.ContentCore.IntegrationTests/FhirPersistenceTests.cs)<br>`FN_CC_004_two_versions_are_two_resources_on_the_server_not_an_overwrite` (src/Epi.ContentCore.IntegrationTests/FhirPersistenceTests.cs) |
| FN-CC-005 | Retrieve a document by canonical identifier and version | Content Core (FHIR) | CAP-SCM-001 | 1 | verified | `FN_CC_005_retrieves_a_specific_version_and_the_latest` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_005_returns_nothing_for_an_unknown_document_or_version` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| FN-CC-006 | Serialise and deserialise a Bundle without content loss | Content Core (FHIR) | CAP-SCM-010 | 1 | verified | `FN_CC_006_preserves_narrative_markup_exactly` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_006_serialises_and_reparses_without_content_loss` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs) |
| FN-CC-007 | Reject any mutation of an existing version | Content Core (FHIR) | CAP-SCM-007 | 1 | verified | `FN_CC_007_a_caller_mutating_a_retrieved_document_does_not_change_the_store` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_007_creating_a_new_version_leaves_the_previous_one_untouched` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_007_rejects_a_bundle_that_already_claims_an_identifier_in_our_namespace` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| FN-CFG-001 | Load market definitions from configuration data | Configuration & Rules Service | CAP-CFG-001, CAP-CFG-004 | 1 | verified | `FN_CFG_001_an_empty_directory_yields_an_empty_catalogue` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_001_exposes_a_market_by_its_code_regardless_of_casing` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_001_loads_every_market_definition_in_the_directory` (src/Epi.Governance.Tests/MarketCatalogueTests.cs) |
| FN-CFG-002 | Resolve the active profile version for a market | Configuration & Rules Service | CAP-CFG-001 | 1 | planned | - |
| FN-CFG-003 | Reject a market definition that fails schema validation | Configuration & Rules Service | CAP-CFG-006 | 1 | verified | `FN_CFG_003_rejects_a_market_with_a_missing_required_field` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_a_market_with_no_languages` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_a_missing_directory_rather_than_starting_empty` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_an_unknown_property_rather_than_ignoring_it` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_malformed_json_naming_the_file` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_two_markets_claiming_the_same_code` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_reports_every_problem_not_only_the_first` (src/Epi.Governance.Tests/MarketCatalogueTests.cs) |
| FN-EVT-001 | Build a content event from a persisted document | Notification & Event Backbone | CAP-EVT-001 | 1 | planned | - |
| FN-EVT-002 | Publish a content event to the event backbone | Notification & Event Backbone | CAP-EVT-001 | 1 | planned | - |
| FN-IAM-001 | Validate an OIDC access token and extract subject claims | IAM | CAP-IAM-001 | 1 | planned | - |
| FN-IAM-002 | Build an authorisation query from subject, action, and resource | IAM | CAP-IAM-002 | 1 | planned | - |
| FN-IAM-003 | Evaluate the policy decision and enforce allow or deny | IAM | CAP-IAM-002 | 1 | planned | - |
| FN-IAM-004 | Apply affiliate and market scope filtering at data access | IAM | CAP-IAM-007 | 1 | planned | - |
| FN-VAL-001 | Check structural well-formedness against the pinned profile | Validation Service | CAP-VAL-003 | 1 | verified | `FN_VAL_001_a_conformant_document_produces_no_errors` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_001_a_missing_required_element_is_an_error` (src/Epi.Validation.Tests/StructuralValidatorTests.cs) |
| FN-VAL-002 | Check reference integrity, rejecting dangling references | Validation Service | CAP-VAL-003 | 1 | verified | `FN_VAL_002_a_reference_satisfied_within_the_document_is_accepted` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_002_a_reference_to_something_absent_from_the_document_is_an_error` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_002_an_external_reference_is_not_treated_as_dangling` (src/Epi.Validation.Tests/StructuralValidatorTests.cs) |
| FN-VAL-003 | Produce structured issues carrying severity and element location | Validation Service | CAP-VAL-005 | 1 | verified | `FN_VAL_003_a_valid_document_reports_no_issues_at_all_rather_than_silence` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_003_an_issue_carries_a_severity_and_the_element_it_is_about` (src/Epi.Validation.Tests/StructuralValidatorTests.cs) |

## Requirements covered by these functions

| Requirement | Design functions | Requirement text |
|---|---|---|
| CAP-AUD-001 | FN-AUD-001 | Capture a comprehensive audit trail of all GxP-relevant actions (who, what, when, why, before/after values). |
| CAP-AUD-002 | FN-AUD-002, FN-AUD-003 | Store audit records tamper-evidently/immutably (append-only). |
| CAP-CFG-001 | FN-CFG-001, FN-CFG-002 | Externalise configuration for markets/regulators, lifecycle state models, workflows, validation/compliance rules, terminology bindings, publishing routing, and event schemas. |
| CAP-CFG-004 | FN-CFG-001 | Onboard a new market/regulator via configuration without a code release. |
| CAP-CFG-006 | FN-CFG-003 | Validate configuration consistency before activation. |
| CAP-EVT-001 | FN-EVT-001, FN-EVT-002 | Provide a publish/subscribe event backbone for inter-capability communication. |
| CAP-IAM-001 | FN-IAM-001 | Authenticate via the enterprise IdP (OIDC/SAML) with SSO/federation and MFA (delegated to IdP). |
| CAP-IAM-002 | FN-IAM-002, FN-IAM-003 | Enforce combined RBAC + ABAC authorization on every action and API. |
| CAP-IAM-007 | FN-IAM-004 | Enforce multi-tenant isolation of affiliate data. |
| CAP-IAM-009 | FN-AUD-004 | Record all access-control decisions and administration changes to audit (#19). |
| CAP-SCM-001 | FN-CC-001, FN-CC-004, FN-CC-005 | Represent an ePI as a FHIR document `Bundle` anchored by a `Composition` with typed, coded sections. |
| CAP-SCM-007 | FN-CC-002, FN-CC-003, FN-CC-007 | Define **canonical identifier and versioning semantics** for documents, sections, and reusable units (stable IDs across languages/markets/versions). |
| CAP-SCM-010 | FN-CC-006 | Preserve full fidelity round-trip: a conformant ePI can be represented and re-serialised without content loss. |
| CAP-VAL-003 | FN-VAL-001, FN-VAL-002 | Validate structural well-formedness and reference integrity (#2), including no dangling references in approval candidates. |
| CAP-VAL-005 | FN-VAL-003 | Produce structured, actionable issues with severity and precise location (section/element). |
