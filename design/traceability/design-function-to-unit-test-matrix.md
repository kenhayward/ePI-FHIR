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
- Verified by at least one unit test: **0**
- Not yet verified: **23**

## Matrix

| Function | Name | Component | Satisfies | Iteration | Status | Unit tests |
|---|---|---|---|---|---|---|
| FN-AUD-001 | Compose an audit record with actor, action, target, before and after values | Audit & e-Signature | CAP-AUD-001 | 1 | planned | - |
| FN-AUD-002 | Append an audit record to the append-only store | Audit & e-Signature | CAP-AUD-002 | 1 | planned | - |
| FN-AUD-003 | Reject update or delete of an existing audit record | Audit & e-Signature | CAP-AUD-002 | 1 | planned | - |
| FN-AUD-004 | Record every access-control decision to the audit sink | Audit & e-Signature | CAP-IAM-009 | 1 | planned | - |
| FN-CC-001 | Parse an ePI document Bundle anchored by a Composition | Content Core (FHIR) | CAP-SCM-001 | 1 | planned | - |
| FN-CC-002 | Assign a canonical identifier to a document | Content Core (FHIR) | CAP-SCM-007 | 1 | planned | - |
| FN-CC-003 | Create an immutable version snapshot and record its lineage | Content Core (FHIR) | CAP-SCM-007 | 1 | planned | - |
| FN-CC-004 | Persist canonical content through the FHIR REST API | Content Core (FHIR) | CAP-SCM-001 | 1 | planned | - |
| FN-CC-005 | Retrieve a document by canonical identifier and version | Content Core (FHIR) | CAP-SCM-001 | 1 | planned | - |
| FN-CC-006 | Serialise and deserialise a Bundle without content loss | Content Core (FHIR) | CAP-SCM-010 | 1 | planned | - |
| FN-CC-007 | Reject any mutation of an existing version | Content Core (FHIR) | CAP-SCM-007 | 1 | planned | - |
| FN-CFG-001 | Load market definitions from configuration data | Configuration & Rules Service | CAP-CFG-001, CAP-CFG-004 | 1 | planned | - |
| FN-CFG-002 | Resolve the active profile version for a market | Configuration & Rules Service | CAP-CFG-001 | 1 | planned | - |
| FN-CFG-003 | Reject a market definition that fails schema validation | Configuration & Rules Service | CAP-CFG-006 | 1 | planned | - |
| FN-EVT-001 | Build a content event from a persisted document | Notification & Event Backbone | CAP-EVT-001 | 1 | planned | - |
| FN-EVT-002 | Publish a content event to the event backbone | Notification & Event Backbone | CAP-EVT-001 | 1 | planned | - |
| FN-IAM-001 | Validate an OIDC access token and extract subject claims | IAM | CAP-IAM-001 | 1 | planned | - |
| FN-IAM-002 | Build an authorisation query from subject, action, and resource | IAM | CAP-IAM-002 | 1 | planned | - |
| FN-IAM-003 | Evaluate the policy decision and enforce allow or deny | IAM | CAP-IAM-002 | 1 | planned | - |
| FN-IAM-004 | Apply affiliate and market scope filtering at data access | IAM | CAP-IAM-007 | 1 | planned | - |
| FN-VAL-001 | Check structural well-formedness against the pinned profile | Validation Service | CAP-VAL-003 | 1 | planned | - |
| FN-VAL-002 | Check reference integrity, rejecting dangling references | Validation Service | CAP-VAL-003 | 1 | planned | - |
| FN-VAL-003 | Produce structured issues carrying severity and element location | Validation Service | CAP-VAL-005 | 1 | planned | - |

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
