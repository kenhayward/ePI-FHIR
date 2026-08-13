# V-Model Trace

GENERATED FILE - do not edit by hand. Regenerate with
`python tools/build-traceability.py`; CI fails if it is out of date.

The whole chain in one place, for a scheduled requirement:

```
  Requirement  --------------------------------->  Integration test
  (specs/capabilities)                             (validates the requirement)
        |                                                    ^
        v                                                    |
  Design function  ----------------------------->  Unit test
  (design-functions.json)                          (verifies the function)
```

Only requirements scheduled in `delivery-map.json` appear below; the full requirement
set is in [requirements-traceability-matrix.md](requirements-traceability-matrix.md).

## Coverage of scheduled requirements

- Scheduled requirements: **15**
- With at least one design function: **15**
- With at least one integration test: **15**
- Design functions awaiting a unit test: **11** of 23
- Integration tests awaiting implementation: **4** of 9

## Trace

| Requirement | Phase | Iteration | Design functions | Unit tests | Integration tests | Implementations |
|---|---|---|---|---|---|---|
| CAP-SCM-001 | P0 | 1 | FN-CC-001, FN-CC-004, FN-CC-005 | `FN_CC_001_reads_a_document_bundle_anchored_by_a_composition` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_a_bundle_that_is_not_of_type_document` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_a_bundle_with_no_entries` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_a_document_bundle_whose_first_entry_is_not_a_composition` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_content_carrying_elements_that_are_not_in_the_model` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_content_that_is_not_a_bundle` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_001_rejects_malformed_json_without_leaking_a_parser_stack_trace` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_004_content_persists_on_the_server_and_is_readable_by_a_new_client` (src/Epi.ContentCore.IntegrationTests/FhirPersistenceTests.cs)<br>`FN_CC_004_the_server_assigns_its_own_identifiers_which_we_do_not_use_as_identity` (src/Epi.ContentCore.IntegrationTests/FhirPersistenceTests.cs)<br>`FN_CC_004_two_versions_are_two_resources_on_the_server_not_an_overwrite` (src/Epi.ContentCore.IntegrationTests/FhirPersistenceTests.cs)<br>`FN_CC_005_retrieves_a_specific_version_and_the_latest` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_005_returns_nothing_for_an_unknown_document_or_version` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) | IT-001 | `IT_001_a_conformant_bundle_round_trips_through_create_and_read_without_content_loss` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| CAP-SCM-007 | P0 | 1 | FN-CC-002, FN-CC-003, FN-CC-007 | `FN_CC_002_assigns_a_canonical_identifier_the_caller_did_not_supply` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_002_encodes_no_business_meaning_in_the_identifier` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_002_mints_a_distinct_identifier_for_every_document` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_003_records_the_identifier_on_the_stored_bundle` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_003_rejects_a_new_version_of_a_document_that_does_not_exist` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_003_starts_at_version_one_and_increments_monotonically` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_007_a_caller_mutating_a_retrieved_document_does_not_change_the_store` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_007_creating_a_new_version_leaves_the_previous_one_untouched` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs)<br>`FN_CC_007_rejects_a_bundle_that_already_claims_an_identifier_in_our_namespace` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) | IT-006 | `IT_006_an_attempt_to_mutate_an_existing_version_is_rejected_and_history_is_reconstructable` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| CAP-SCM-010 | P0 | 1 | FN-CC-006 | `FN_CC_006_preserves_narrative_markup_exactly` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs)<br>`FN_CC_006_serialises_and_reparses_without_content_loss` (src/Epi.ContentCore.Tests/EpiBundleReaderTests.cs) | IT-001 | `IT_001_a_conformant_bundle_round_trips_through_create_and_read_without_content_loss` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| CAP-VAL-003 | P2 | 1 | FN-VAL-001, FN-VAL-002 | `FN_VAL_001_a_conformant_document_produces_no_errors` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_001_a_missing_required_element_is_an_error` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_002_a_reference_satisfied_within_the_document_is_accepted` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_002_a_reference_to_something_absent_from_the_document_is_an_error` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_002_an_external_reference_is_not_treated_as_dangling` (src/Epi.Validation.Tests/StructuralValidatorTests.cs) | IT-005 | `IT_005_a_rejected_new_version_leaves_the_previous_version_intact` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_malformed_content_is_rejected_with_itemised_located_errors` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_rejected_content_leaves_no_partial_state` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_valid_content_passes_the_gate_and_is_stored` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs) |
| CAP-VAL-005 | P2 | 1 | FN-VAL-003 | `FN_VAL_003_a_valid_document_reports_no_issues_at_all_rather_than_silence` (src/Epi.Validation.Tests/StructuralValidatorTests.cs)<br>`FN_VAL_003_an_issue_carries_a_severity_and_the_element_it_is_about` (src/Epi.Validation.Tests/StructuralValidatorTests.cs) | IT-005 | `IT_005_a_rejected_new_version_leaves_the_previous_version_intact` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_malformed_content_is_rejected_with_itemised_located_errors` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_rejected_content_leaves_no_partial_state` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_valid_content_passes_the_gate_and_is_stored` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs) |
| CAP-IAM-001 | P0 | 1 | FN-IAM-001 | - | IT-007 | - |
| CAP-IAM-002 | P0 | 1 | FN-IAM-002, FN-IAM-003 | - | IT-002 | - |
| CAP-IAM-007 | P0 | 1 | FN-IAM-004 | - | IT-002 | - |
| CAP-IAM-009 | P0 | 1 | FN-AUD-004 | - | IT-003 | - |
| CAP-AUD-001 | P0 | 1 | FN-AUD-001 | - | IT-003 | - |
| CAP-AUD-002 | P0 | 1 | FN-AUD-002, FN-AUD-003 | - | IT-003 | - |
| CAP-EVT-001 | P2 | 1 | FN-EVT-001, FN-EVT-002 | - | IT-008 | - |
| CAP-CFG-001 | P0 | 1 | FN-CFG-001, FN-CFG-002 | `FN_CFG_001_an_empty_directory_yields_an_empty_catalogue` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_001_exposes_a_market_by_its_code_regardless_of_casing` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_001_loads_every_market_definition_in_the_directory` (src/Epi.Governance.Tests/MarketCatalogueTests.cs) | IT-004 | `IT_004_a_new_market_is_added_by_configuration_alone` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs)<br>`IT_004_the_shipped_market_configuration_loads_and_validates` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |
| CAP-CFG-004 | P0 | 1 | FN-CFG-001 | `FN_CFG_001_an_empty_directory_yields_an_empty_catalogue` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_001_exposes_a_market_by_its_code_regardless_of_casing` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_001_loads_every_market_definition_in_the_directory` (src/Epi.Governance.Tests/MarketCatalogueTests.cs) | IT-004 | `IT_004_a_new_market_is_added_by_configuration_alone` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs)<br>`IT_004_the_shipped_market_configuration_loads_and_validates` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |
| CAP-CFG-006 | P0 | 1 | FN-CFG-003 | `FN_CFG_003_rejects_a_market_with_a_missing_required_field` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_a_market_with_no_languages` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_a_missing_directory_rather_than_starting_empty` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_an_unknown_property_rather_than_ignoring_it` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_malformed_json_naming_the_file` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_rejects_two_markets_claiming_the_same_code` (src/Epi.Governance.Tests/MarketCatalogueTests.cs)<br>`FN_CFG_003_reports_every_problem_not_only_the_first` (src/Epi.Governance.Tests/MarketCatalogueTests.cs) | IT-009 | `IT_009_an_invalid_market_definition_is_rejected_before_activation` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |

## Gaps

Gaps are reported, not enforced: a requirement scheduled but not yet decomposed is
normal work in progress. Referential errors, by contrast, fail the build.

**Scheduled requirements with no design function:** none

**Scheduled requirements with no integration test:** none

**Design functions with no unit test:** FN-AUD-001, FN-AUD-002, FN-AUD-003, FN-AUD-004, FN-CFG-002, FN-EVT-001, FN-EVT-002, FN-IAM-001, FN-IAM-002, FN-IAM-003, FN-IAM-004

**Integration tests not yet implemented:** IT-002, IT-003, IT-007, IT-008
