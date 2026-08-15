# Requirement to Integration Test Matrix

GENERATED FILE - do not edit by hand. Regenerate with
`python tools/build-traceability.py`; CI fails if it is out of date.

The **ascent of the V**: integration tests validate capability requirements end to end,
against real collaborators rather than mocks. Intent is declared in
`integration-tests.json`; the implementing test is discovered from the code by its IT id.

Name integration tests for the case they validate, for example
`IT_001_bundle_round_trips_through_create_and_read`.

## Coverage

- Integration tests declared: **18**
- Implemented in code: **16**
- Requirements validated by at least one integration test: **28**
- Scheduled requirements still without one: **9**

## Integration tests

| Test | Scenario | Verifies | Iteration | Status | Implementation |
|---|---|---|---|---|---|
| IT-001 | A conformant ePI Bundle round-trips through create and read with no content loss | CAP-SCM-010, CAP-SCM-001 | 1 | implemented | `IT_001_a_conformant_bundle_round_trips_through_create_and_read_without_content_loss` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| IT-002 | A caller outside the resource affiliate or market is denied and sees nothing | CAP-IAM-002, CAP-IAM-007 | 1 | implemented | `IT_002_a_role_without_the_action_is_denied` (src/Epi.Iam.IntegrationTests/AuthorizationTests.cs)<br>`IT_002_a_subject_from_another_affiliate_is_denied` (src/Epi.Iam.IntegrationTests/AuthorizationTests.cs)<br>`IT_002_a_subject_in_scope_with_the_right_role_is_allowed` (src/Epi.Iam.IntegrationTests/AuthorizationTests.cs)<br>`IT_002_a_subject_without_the_market_in_scope_is_denied` (src/Epi.Iam.IntegrationTests/AuthorizationTests.cs)<br>`IT_002_an_independent_approver_is_allowed` (src/Epi.Iam.IntegrationTests/AuthorizationTests.cs)<br>`IT_002_segregation_of_duties_denies_an_author_approving_their_own_label` (src/Epi.Iam.IntegrationTests/AuthorizationTests.cs) |
| IT-003 | A content write and an access decision each produce an immutable audit record | CAP-AUD-001, CAP-AUD-002, CAP-IAM-009 | 1 | implemented | `IT_003_a_content_write_produces_a_record_with_before_and_after` (src/Epi.Governance.Tests/AuditTests.cs)<br>`IT_003_a_failed_write_is_recorded_too` (src/Epi.Governance.Tests/AuditTests.cs) |
| IT-004 | A second market is served by adding configuration alone, with no code change | CAP-CFG-004, CAP-CFG-001 | 1 | implemented | `IT_004_a_new_market_is_added_by_configuration_alone` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs)<br>`IT_004_the_shipped_market_configuration_loads_and_validates` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |
| IT-005 | Malformed content is rejected with itemised located errors and leaves no partial state | CAP-VAL-003, CAP-VAL-005 | 1 | implemented | `IT_005_a_rejected_new_version_leaves_the_previous_version_intact` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_malformed_content_is_rejected_with_itemised_located_errors` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_rejected_content_leaves_no_partial_state` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs)<br>`IT_005_valid_content_passes_the_gate_and_is_stored` (src/Epi.Validation.Tests/ValidatingContentStoreTests.cs) |
| IT-006 | An attempt to mutate an existing version is rejected and history remains reconstructable | CAP-SCM-007 | 1 | implemented | `IT_006_an_attempt_to_mutate_an_existing_version_is_rejected_and_history_is_reconstructable` (src/Epi.ContentCore.Tests/ContentStoreConformance.cs) |
| IT-007 | A request without a valid OIDC token is rejected before reaching content | CAP-IAM-001 | 1 | implemented | `IT_007_a_request_without_a_token_is_refused` (src/Epi.Api.Tests/ContentEndpointTests.cs)<br>`IT_007_reading_without_a_token_is_refused_too` (src/Epi.Api.Tests/ContentEndpointTests.cs)<br>`IT_007_the_health_probe_stays_open` (src/Epi.Api.Tests/ContentEndpointTests.cs) |
| IT-008 | Creating a document emits a content event to the backbone | CAP-EVT-001 | 1 | implemented | `IT_008_creating_a_document_emits_an_event_naming_it` (src/Epi.Governance.Tests/ContentEventTests.cs) |
| IT-009 | An invalid market definition is rejected before activation | CAP-CFG-006 | 1 | implemented | `IT_009_an_invalid_market_definition_is_rejected_before_activation` (src/Epi.Governance.Tests/MarketConfigurationIntegrationTests.cs) |
| IT-010 | An unpermitted transition is rejected and a permitted one records actor and timestamp | CAP-LCM-001, CAP-LCM-007 | 2 | implemented | `IT_010_a_permitted_transition_records_actor_and_time` (src/Epi.Lifecycle.Tests/LifecycleServiceTests.cs)<br>`IT_010_a_refused_transition_leaves_no_history_behind` (src/Epi.Lifecycle.Tests/LifecycleServiceTests.cs)<br>`IT_010_a_transition_is_refused_without_a_token` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_010_a_transition_the_model_does_not_permit_is_refused` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_010_a_transition_the_model_does_not_permit_is_refused` (src/Epi.Lifecycle.Tests/LifecycleServiceTests.cs)<br>`IT_010_an_approval_without_a_signature_is_refused` (src/Epi.Api.Tests/SigningEndpointTests.cs) |
| IT-011 | The author of a version cannot approve it, by any route | CAP-IAM-006, CAP-WFL-005 | 2 | implemented | `IT_011_an_unknown_author_refuses_approval_rather_than_allowing_it` (src/Epi.Lifecycle.Tests/LifecycleServiceTests.cs)<br>`IT_011_someone_other_than_the_author_may_approve_it` (src/Epi.Lifecycle.Tests/LifecycleServiceTests.cs)<br>`IT_011_the_author_cannot_approve_their_own_version_over_http` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_011_the_author_of_a_version_may_not_approve_it` (src/Epi.Lifecycle.Tests/LifecycleServiceTests.cs) |
| IT-012 | Approval captures a signature binding signer, meaning, time and a hash of the version signed | CAP-AUD-003, CAP-WFL-003 | 2 | implemented | `IT_012_a_signature_by_the_author_cannot_approve_their_own_version` (src/Epi.Signature.Tests/SignatureCheckTests.cs)<br>`IT_012_a_signature_cannot_be_spent_twice` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_012_a_signature_may_only_be_made_over_content_the_signer_may_see` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_012_a_wrong_password_is_refused_without_saying_what_was_wrong` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_012_an_author_submits_and_someone_else_signs_and_approves` (src/Epi.Api.Tests/SigningEndpointTests.cs)<br>`IT_012_approval_captures_a_signature_over_the_exact_version_signed` (src/Epi.Signature.Tests/SignatureCheckTests.cs) |
| IT-013 | A version approved in one market is not approved in another, on the same content | CAP-LCM-003 | 2 | implemented | `IT_013_a_rejection_in_one_market_leaves_the_other_untouched` (src/Epi.Lifecycle.Tests/MarketApprovalServiceTests.cs)<br>`IT_013_a_version_approved_in_one_market_is_not_approved_in_another` (src/Epi.Lifecycle.Tests/MarketApprovalServiceTests.cs) |
| IT-014 | A label instantiated from a template validates and records its template version | CAP-TPL-004, CAP-TPL-007 | 2 | planned | - |
| IT-015 | Section identifiers survive a new version unchanged | CAP-SCM-007 | 2 | planned | - |
| IT-016 | Search returns only what the caller may see and can return the current-approved version per market | CAP-SCH-002, CAP-SCH-004 | 2 | implemented | `IT_016_a_current_approved_version_outside_the_callers_scope_is_not_found` (src/Epi.Api.Tests/SearchEndpointTests.cs)<br>`IT_016_a_search_returns_the_content_the_caller_may_see` (src/Epi.Api.Tests/SearchEndpointTests.cs)<br>`IT_016_content_in_a_market_the_caller_does_not_hold_is_invisible` (src/Epi.Api.Tests/SearchEndpointTests.cs)<br>`IT_016_the_current_approved_version_is_the_one_the_market_approved` (src/Epi.Api.Tests/SearchEndpointTests.cs) |
| IT-017 | A historical version is reconstructable with the metadata that made it valid | CAP-LCM-006 | 2 | implemented | `IT_017_an_approved_version_reconstructs_with_what_it_was_approved_against` (src/Epi.Api.Tests/ReconstructionEndpointTests.cs)<br>`IT_017_the_reconstruction_carries_the_whole_history_and_the_signature_used` (src/Epi.Api.Tests/ReconstructionEndpointTests.cs)<br>`IT_017_the_reconstruction_reports_whether_the_pinned_packages_still_match` (src/Epi.Api.Tests/ReconstructionEndpointTests.cs) |
| IT-018 | A regulatory submission is refused unsigned; recording the regulator's decision needs no signature | CAP-LCM-012 | 2 | implemented | `IT_018_a_submission_without_a_signature_is_refused` (src/Epi.Lifecycle.Tests/MarketSubmissionSignatureTests.cs)<br>`IT_018_recording_the_regulators_decision_needs_no_signature` (src/Epi.Lifecycle.Tests/MarketSubmissionSignatureTests.cs) |

## By requirement

| Requirement | Integration tests | Requirement text |
|---|---|---|
| CAP-AUD-001 | IT-003 | Capture a comprehensive audit trail of all GxP-relevant actions (who, what, when, why, before/after values). |
| CAP-AUD-002 | IT-003 | Store audit records tamper-evidently/immutably (append-only). |
| CAP-AUD-003 | IT-012 | Provide electronic signatures per 21 CFR Part 11 & EU Annex 11: signing events, signature meaning/manifest, and binding to the signed record and signer identity. |
| CAP-CFG-001 | IT-004 | Externalise configuration for markets/regulators, lifecycle state models, workflows, validation/compliance rules, terminology bindings, publishing routing, and event schemas. |
| CAP-CFG-004 | IT-004 | Onboard a new market/regulator via configuration without a code release. |
| CAP-CFG-006 | IT-009 | Validate configuration consistency before activation. |
| CAP-EVT-001 | IT-008 | Provide a publish/subscribe event backbone for inter-capability communication. |
| CAP-IAM-001 | IT-007 | Authenticate via the enterprise IdP (OIDC/SAML) with SSO/federation and MFA (delegated to IdP). |
| CAP-IAM-002 | IT-002 | Enforce combined RBAC + ABAC authorization on every action and API. |
| CAP-IAM-006 | IT-011 | Enforce segregation of duties (e.g. author != approver) across workflows (#16). |
| CAP-IAM-007 | IT-002 | Enforce multi-tenant isolation of affiliate data. |
| CAP-IAM-009 | IT-003 | Record all access-control decisions and administration changes to audit (#19). |
| CAP-LCM-001 | IT-010 | Provide a configurable lifecycle state model with explicitly allowed transitions. |
| CAP-LCM-003 | IT-013 | Maintain **internal lifecycle state** separately from **per-market regulatory-approval state**. |
| CAP-LCM-006 | IT-017 | Reconstruct the full content and metadata of any historical version. |
| CAP-LCM-007 | IT-010 | Enforce transitions through workflow (#16) and permissions/segregation of duties (#17). |
| CAP-LCM-012 | IT-018 | Require an electronic signature (#19) to submit a version to a regulator. Recording a regulator's subsequent decision is a factual entry about an external event and is **not** signed. |
| CAP-SCH-002 | IT-016 | Retrieve a specific version and the current-approved version of a label per market/language. |
| CAP-SCH-004 | IT-016 | Scope all results by caller permissions/attributes (#17); never leak out-of-scope content. |
| CAP-SCM-001 | IT-001 | Represent an ePI as a FHIR document `Bundle` anchored by a `Composition` with typed, coded sections. |
| CAP-SCM-007 | IT-006, IT-015 | Define **canonical identifier and versioning semantics** for documents, sections, and reusable units (stable IDs across languages/markets/versions). |
| CAP-SCM-010 | IT-001 | Preserve full fidelity round-trip: a conformant ePI can be represented and re-serialised without content loss. |
| CAP-TPL-004 | IT-014 | Instantiate a new label from a template, producing a conformant, pre-scaffolded draft handed to #7. |
| CAP-TPL-007 | IT-014 | Version templates with effective dates; record which template (and version) each label was instantiated from. |
| CAP-VAL-003 | IT-005 | Validate structural well-formedness and reference integrity (#2), including no dangling references in approval candidates. |
| CAP-VAL-005 | IT-005 | Produce structured, actionable issues with severity and precise location (section/element). |
| CAP-WFL-003 | IT-012 | Invoke electronic signature at approval gates (#19). |
| CAP-WFL-005 | IT-011 | Drive lifecycle transitions in #7 on step completion. |
