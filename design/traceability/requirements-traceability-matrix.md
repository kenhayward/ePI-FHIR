# Requirements Traceability Matrix

GENERATED FILE - do not edit by hand. Regenerate with
`python tools/build-traceability.py`; CI fails if it is out of date.

Requirement text, IDs, and priority come from the capability specifications; phase from
D1 Section 11 (cross-checked against the D2 group summaries); component from D3 Section
2.1. Delivery evidence is hand-maintained in `delivery-map.json`.

This is the cross-cutting artefact called for in the Deliverables Definition Section 8.
For the full V model see [v-model-trace.md](v-model-trace.md).

## Coverage

- Requirements specified: **220** across **24** capabilities
- Requirements with delivery evidence: **29**
- Requirements not yet scheduled: **191**

## Capabilities

| # | Capability | Abbr | Phase | Also in | Component (D3 Section 2.1) | Requirements | Specification |
|---|---|---|---|---|---|---|---|
| 1 | Ingestion & Import | ING | P1 | - | Authoring & Template Service | 14 | [D2.1-content-and-authoring.md](../../specs/capabilities/D2.1-content-and-authoring.md) |
| 2 | Structured Content Model | SCM | P0 | - | Content Core (FHIR) | 12 | [D2.1-content-and-authoring.md](../../specs/capabilities/D2.1-content-and-authoring.md) |
| 3 | Template & Label-Type Management | TPL | P1 | - | Authoring & Template Service | 12 | [D2.1-content-and-authoring.md](../../specs/capabilities/D2.1-content-and-authoring.md) |
| 4 | Data Migration & Legacy Onboarding | MIG | P4 | - | Migration Service | 12 | [D2.1-content-and-authoring.md](../../specs/capabilities/D2.1-content-and-authoring.md) |
| 5 | Master Data & Identifiers | MDM | P0 | - | Master Data Service | 10 | [D2.2-reference-and-master-data.md](../../specs/capabilities/D2.2-reference-and-master-data.md) |
| 6 | Terminology & Code System Management | TRM | P0 | - | Terminology Service | 10 | [D2.2-reference-and-master-data.md](../../specs/capabilities/D2.2-reference-and-master-data.md) |
| 7 | Lifecycle & Version Management | LCM | P1 | - | Lifecycle & Workflow Service | 11 | [D2.3-lifecycle-change-localisation.md](../../specs/capabilities/D2.3-lifecycle-change-localisation.md) |
| 8 | Change Management & Impact Analysis | CHG | P3 | - | Change & Impact Service | 10 | [D2.3-lifecycle-change-localisation.md](../../specs/capabilities/D2.3-lifecycle-change-localisation.md) |
| 9 | Localisation, Multi-region & Translation Management | LOC | P1 | P3 | Localisation & Translation Service | 9 | [D2.3-lifecycle-change-localisation.md](../../specs/capabilities/D2.3-lifecycle-change-localisation.md) |
| 10 | Regulatory Mapping & Conformance Profiles | REG | P2 | P3 | Regulatory Profiles Service | 8 | [D2.3-lifecycle-change-localisation.md](../../specs/capabilities/D2.3-lifecycle-change-localisation.md) |
| 11 | Validation & Quality | VAL | P2 | - | Validation Service | 8 | [D2.4-quality-and-production.md](../../specs/capabilities/D2.4-quality-and-production.md) |
| 12 | Compliance & Completeness Checking | CMP | P2 | - | Compliance Service | 8 | [D2.4-quality-and-production.md](../../specs/capabilities/D2.4-quality-and-production.md) |
| 13 | Rendering & Transformation | RND | P2 | - | Rendering Service | 9 | [D2.4-quality-and-production.md](../../specs/capabilities/D2.4-quality-and-production.md) |
| 14 | Publishing & Distribution | PUB | P2 | - | Publishing Service | 9 | [D2.4-quality-and-production.md](../../specs/capabilities/D2.4-quality-and-production.md) |
| 15 | Search, Access & Retrieval | SCH | P1 | P2 | Search Service | 8 | [D2.5-access-and-governance.md](../../specs/capabilities/D2.5-access-and-governance.md) |
| 16 | Workflow & Approvals | WFL | P1 | - | Lifecycle & Workflow Service | 8 | [D2.5-access-and-governance.md](../../specs/capabilities/D2.5-access-and-governance.md) |
| 17 | Identity, Access Control & Permissions - RBAC/ABAC | IAM | P0 | - | IAM | 9 | [D2.5-access-and-governance.md](../../specs/capabilities/D2.5-access-and-governance.md) |
| 18 | Security | SEC | P0 | - | Security | 7 | [D2.5-access-and-governance.md](../../specs/capabilities/D2.5-access-and-governance.md) |
| 19 | Audit Trail, e-Signature & Inspection Support | AUD | P0 | - | Audit & e-Signature | 9 | [D2.5-access-and-governance.md](../../specs/capabilities/D2.5-access-and-governance.md) |
| 20 | Notifications, Events & Subscriptions | EVT | P2 | - | Notification & Event Backbone | 8 | [D2.6-platform-and-operations.md](../../specs/capabilities/D2.6-platform-and-operations.md) |
| 21 | Configuration & Business-Rule Management | CFG | P0 | - | Configuration & Rules Service | 7 | [D2.6-platform-and-operations.md](../../specs/capabilities/D2.6-platform-and-operations.md) |
| 22 | Records Retention & Archival | RET | P4 | - | Retention & Archival Service | 7 | [D2.6-platform-and-operations.md](../../specs/capabilities/D2.6-platform-and-operations.md) |
| 23 | Reporting & Analytics | RPT | P4 | - | Reporting & Analytics | 7 | [D2.6-platform-and-operations.md](../../specs/capabilities/D2.6-platform-and-operations.md) |
| 24 | External Integration | INT | P3 | - | Integration & Adapters | 8 | [D2.6-platform-and-operations.md](../../specs/capabilities/D2.6-platform-and-operations.md) |

## Matrix

Status values: `planned` (scheduled, not built), `partial` (some aspect delivered),
`done` (delivered with evidence), `-` (not yet scheduled).

| Requirement | Cap | Pri | Phase | Component | Iteration | Status | Evidence |
|---|---|---|---|---|---|---|---|
| CAP-ING-001 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-002 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-003 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-004 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-005 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-006 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-007 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-008 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-009 | 1 | S | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-010 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-011 | 1 | S | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-012 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-013 | 1 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-ING-014 | 1 | S | P1 | Authoring & Template Service | - | - | - |
| CAP-SCM-001 | 2 | M | P0 | Content Core (FHIR) | 1 | partial | EpiBundleReader plus the store conformance suite run against both the in-memory store and HAPI; coded section typing is a later iteration |
| CAP-SCM-002 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-003 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-004 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-005 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-006 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-007 | 2 | M | P0 | Content Core (FHIR) | 1-2 | partial | ADR-015 plus FN-CC-002/003/007 for documents and FN-CC-008/009 for sections; reusable-unit identity is a later iteration |
| CAP-SCM-008 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-009 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-010 | 2 | M | P0 | Content Core (FHIR) | 1 | partial | FN_CC_006 and IT_001, the latter run against both the in-memory store and a real HAPI server; validation against the pinned profile is PR 5 |
| CAP-SCM-011 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-SCM-012 | 2 | M | P0 | Content Core (FHIR) | - | - | - |
| CAP-TPL-001 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-002 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-003 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-004 | 3 | M | P1 | Authoring & Template Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-TPL-005 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-006 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-007 | 3 | M | P1 | Authoring & Template Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-TPL-008 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-009 | 3 | M | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-010 | 3 | S | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-011 | 3 | S | P1 | Authoring & Template Service | - | - | - |
| CAP-TPL-012 | 3 | S | P1 | Authoring & Template Service | - | - | - |
| CAP-MIG-001 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-002 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-003 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-004 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-005 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-006 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-007 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-008 | 4 | M | P4 | Migration Service | - | - | - |
| CAP-MIG-009 | 4 | S | P4 | Migration Service | - | - | - |
| CAP-MIG-010 | 4 | S | P4 | Migration Service | - | - | - |
| CAP-MIG-011 | 4 | S | P4 | Migration Service | - | - | - |
| CAP-MIG-012 | 4 | S | P4 | Migration Service | - | - | - |
| CAP-MDM-001 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-002 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-003 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-004 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-005 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-006 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-007 | 5 | S | P0 | Master Data Service | - | - | - |
| CAP-MDM-008 | 5 | M | P0 | Master Data Service | - | - | - |
| CAP-MDM-009 | 5 | S | P0 | Master Data Service | - | - | - |
| CAP-MDM-010 | 5 | S | P0 | Master Data Service | - | - | - |
| CAP-TRM-001 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-002 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-003 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-004 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-005 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-006 | 6 | S | P0 | Terminology Service | - | - | - |
| CAP-TRM-007 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-008 | 6 | S | P0 | Terminology Service | - | - | - |
| CAP-TRM-009 | 6 | M | P0 | Terminology Service | - | - | - |
| CAP-TRM-010 | 6 | S | P0 | Terminology Service | - | - | - |
| CAP-LCM-001 | 7 | M | P1 | Lifecycle & Workflow Service | 2 | partial | FN_LCM_001/002 and IT_010: model loaded from configuration and unpermitted transitions refused |
| CAP-LCM-002 | 7 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-LCM-003 | 7 | M | P1 | Lifecycle & Workflow Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-LCM-004 | 7 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-LCM-005 | 7 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-LCM-006 | 7 | M | P1 | Lifecycle & Workflow Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-LCM-007 | 7 | M | P1 | Lifecycle & Workflow Service | 2 | partial | FN_LCM_003: transitions recorded with actor, time and reason; workflow routing is a later PR |
| CAP-LCM-008 | 7 | S | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-LCM-009 | 7 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-LCM-010 | 7 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-LCM-011 | 7 | M | P1 | Lifecycle & Workflow Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-CHG-001 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-002 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-003 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-004 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-005 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-006 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-007 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-008 | 8 | S | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-009 | 8 | M | P3 | Change & Impact Service | - | - | - |
| CAP-CHG-010 | 8 | S | P3 | Change & Impact Service | - | - | - |
| CAP-LOC-001 | 9 | M | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-002 | 9 | M | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-003 | 9 | M | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-004 | 9 | S | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-005 | 9 | M | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-006 | 9 | M | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-007 | 9 | M | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-008 | 9 | C | P1 | Localisation & Translation Service | - | - | - |
| CAP-LOC-009 | 9 | S | P1 | Localisation & Translation Service | - | - | - |
| CAP-REG-001 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-002 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-003 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-004 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-005 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-006 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-007 | 10 | M | P2 | Regulatory Profiles Service | - | - | - |
| CAP-REG-008 | 10 | S | P2 | Regulatory Profiles Service | - | - | - |
| CAP-VAL-001 | 11 | M | P2 | Validation Service | - | - | - |
| CAP-VAL-002 | 11 | M | P2 | Validation Service | - | - | - |
| CAP-VAL-003 | 11 | M | P2 | Validation Service | 1 | partial | FN_VAL_001 and FN_VAL_002 at the write gate; other gates (ingest, pre-approval, pre-publish) arrive with the capabilities that own them |
| CAP-VAL-004 | 11 | M | P2 | Validation Service | - | - | - |
| CAP-VAL-005 | 11 | M | P2 | Validation Service | 1 | partial | FN_VAL_003 and IT_005: issues carry severity and a FHIRPath location; severity configurability per gate and market is CAP-VAL-007, later |
| CAP-VAL-006 | 11 | S | P2 | Validation Service | - | - | - |
| CAP-VAL-007 | 11 | S | P2 | Validation Service | - | - | - |
| CAP-VAL-008 | 11 | M | P2 | Validation Service | - | - | - |
| CAP-CMP-001 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-002 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-003 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-004 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-005 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-006 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-007 | 12 | M | P2 | Compliance Service | - | - | - |
| CAP-CMP-008 | 12 | S | P2 | Compliance Service | - | - | - |
| CAP-RND-001 | 13 | M | P2 | Rendering Service | - | - | - |
| CAP-RND-002 | 13 | M | P2 | Rendering Service | - | - | - |
| CAP-RND-003 | 13 | M | P2 | Rendering Service | - | - | - |
| CAP-RND-004 | 13 | M | P2 | Rendering Service | - | - | - |
| CAP-RND-005 | 13 | S | P2 | Rendering Service | - | - | - |
| CAP-RND-006 | 13 | S | P2 | Rendering Service | - | - | - |
| CAP-RND-007 | 13 | M | P2 | Rendering Service | - | - | - |
| CAP-RND-008 | 13 | S | P2 | Rendering Service | - | - | - |
| CAP-RND-009 | 13 | M | P2 | Rendering Service | - | - | - |
| CAP-PUB-001 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-002 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-003 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-004 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-005 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-006 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-007 | 14 | S | P2 | Publishing Service | - | - | - |
| CAP-PUB-008 | 14 | M | P2 | Publishing Service | - | - | - |
| CAP-PUB-009 | 14 | S | P2 | Publishing Service | - | - | - |
| CAP-SCH-001 | 15 | M | P1 | Search Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-SCH-002 | 15 | M | P1 | Search Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-SCH-003 | 15 | M | P1 | Search Service | - | - | - |
| CAP-SCH-004 | 15 | M | P1 | Search Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-SCH-005 | 15 | M | P1 | Search Service | - | - | - |
| CAP-SCH-006 | 15 | S | P1 | Search Service | - | - | - |
| CAP-SCH-007 | 15 | S | P1 | Search Service | - | - | - |
| CAP-SCH-008 | 15 | S | P1 | Search Service | - | - | - |
| CAP-WFL-001 | 16 | M | P1 | Lifecycle & Workflow Service | 2 | planned | iteration-2 (see design/iteration-2.md) |
| CAP-WFL-002 | 16 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-WFL-003 | 16 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-WFL-004 | 16 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-WFL-005 | 16 | M | P1 | Lifecycle & Workflow Service | 2 | partial | IT_011 for the approval gate; routing and escalation are later |
| CAP-WFL-006 | 16 | S | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-WFL-007 | 16 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-WFL-008 | 16 | M | P1 | Lifecycle & Workflow Service | - | - | - |
| CAP-IAM-001 | 17 | M | P0 | IAM | 1 | partial | FN_IAM_001 plus bearer authentication on every content endpoint (IT_007); federation and MFA are the identity provider's, per CAP-IAM-001 |
| CAP-IAM-002 | 17 | M | P0 | IAM | 1 | partial | FN_IAM_002 and FN_IAM_003, IT_002 against real OPA, and enforcement on every content endpoint; other capabilities enforce as they arrive |
| CAP-IAM-003 | 17 | M | P0 | IAM | - | - | - |
| CAP-IAM-004 | 17 | M | P0 | IAM | - | - | - |
| CAP-IAM-005 | 17 | M | P0 | IAM | - | - | - |
| CAP-IAM-006 | 17 | M | P0 | IAM | 2 | partial | IT_011: the author of a version cannot approve it, enforced in the one path every transition takes |
| CAP-IAM-007 | 17 | M | P0 | IAM | 1 | partial | FN_IAM_004 on every store operation, reached through the API; physical partitioning for residency markets is ADR-004, later |
| CAP-IAM-008 | 17 | M | P0 | IAM | - | - | - |
| CAP-IAM-009 | 17 | M | P0 | IAM | 1 | partial | FN_AUD_004: decisions recorded whether allowed or denied; administration changes are recorded when that surface exists |
| CAP-SEC-001 | 18 | M | P0 | Security | - | - | - |
| CAP-SEC-002 | 18 | M | P0 | Security | - | - | - |
| CAP-SEC-003 | 18 | M | P0 | Security | - | - | - |
| CAP-SEC-004 | 18 | M | P0 | Security | - | - | - |
| CAP-SEC-005 | 18 | S | P0 | Security | - | - | - |
| CAP-SEC-006 | 18 | S | P0 | Security | - | - | - |
| CAP-SEC-007 | 18 | S | P0 | Security | - | - | - |
| CAP-AUD-001 | 19 | M | P0 | Audit & e-Signature | 1 | partial | FN_AUD_001 and IT_003 for content writes and access decisions; other capabilities write as they arrive |
| CAP-AUD-002 | 19 | M | P0 | Audit & e-Signature | 1 | partial | FN_AUD_002 and FN_AUD_003: append-only by interface, and enforced by the database in the PostgreSQL sink; sealed WORM export is capability 22 |
| CAP-AUD-003 | 19 | M | P0 | Audit & e-Signature | 2 | partial | ADR-020 plus FN_AUD_005: the manifest binds signer, printed name, meaning, time and a SHA-256 hash of the version, and every signing attempt is audited. Invoking it at the approval gate, and the single-use rule that stops a signature being replayed, are the next pull request; the durable signature store follows |
| CAP-AUD-004 | 19 | M | P0 | Audit & e-Signature | - | - | - |
| CAP-AUD-005 | 19 | M | P0 | Audit & e-Signature | - | - | - |
| CAP-AUD-006 | 19 | M | P0 | Audit & e-Signature | - | - | - |
| CAP-AUD-007 | 19 | M | P0 | Audit & e-Signature | - | - | - |
| CAP-AUD-008 | 19 | M | P0 | Audit & e-Signature | - | - | - |
| CAP-AUD-009 | 19 | M | P0 | Audit & e-Signature | - | - | - |
| CAP-EVT-001 | 20 | M | P2 | Notification & Event Backbone | 1 | partial | FN_EVT_001, FN_EVT_002 and IT_008: emission only, against an in-memory publisher; the broker adapter and delivery guarantees are later |
| CAP-EVT-002 | 20 | M | P2 | Notification & Event Backbone | - | - | - |
| CAP-EVT-003 | 20 | M | P2 | Notification & Event Backbone | - | - | - |
| CAP-EVT-004 | 20 | M | P2 | Notification & Event Backbone | - | - | - |
| CAP-EVT-005 | 20 | M | P2 | Notification & Event Backbone | - | - | - |
| CAP-EVT-006 | 20 | S | P2 | Notification & Event Backbone | - | - | - |
| CAP-EVT-007 | 20 | M | P2 | Notification & Event Backbone | - | - | - |
| CAP-EVT-008 | 20 | M | P2 | Notification & Event Backbone | - | - | - |
| CAP-CFG-001 | 21 | M | P0 | Configuration & Rules Service | 1 | partial | MarketCatalogue with FN_CFG_002 profile binding; lifecycle, workflow and rule config are later iterations |
| CAP-CFG-002 | 21 | M | P0 | Configuration & Rules Service | - | - | - |
| CAP-CFG-003 | 21 | M | P0 | Configuration & Rules Service | - | - | - |
| CAP-CFG-004 | 21 | M | P0 | Configuration & Rules Service | 1 | partial | IT_004_a_new_market_is_added_by_configuration_alone; a market carries no behaviour until PR 6 consumes it |
| CAP-CFG-005 | 21 | M | P0 | Configuration & Rules Service | - | - | - |
| CAP-CFG-006 | 21 | M | P0 | Configuration & Rules Service | 1 | partial | IT_009_an_invalid_market_definition_is_rejected_before_activation; market configuration only |
| CAP-CFG-007 | 21 | M | P0 | Configuration & Rules Service | - | - | - |
| CAP-RET-001 | 22 | M | P4 | Retention & Archival Service | - | - | - |
| CAP-RET-002 | 22 | M | P4 | Retention & Archival Service | - | - | - |
| CAP-RET-003 | 22 | M | P4 | Retention & Archival Service | - | - | - |
| CAP-RET-004 | 22 | M | P4 | Retention & Archival Service | - | - | - |
| CAP-RET-005 | 22 | M | P4 | Retention & Archival Service | - | - | - |
| CAP-RET-006 | 22 | M | P4 | Retention & Archival Service | - | - | - |
| CAP-RET-007 | 22 | S | P4 | Retention & Archival Service | - | - | - |
| CAP-RPT-001 | 23 | M | P4 | Reporting & Analytics | - | - | - |
| CAP-RPT-002 | 23 | M | P4 | Reporting & Analytics | - | - | - |
| CAP-RPT-003 | 23 | S | P4 | Reporting & Analytics | - | - | - |
| CAP-RPT-004 | 23 | S | P4 | Reporting & Analytics | - | - | - |
| CAP-RPT-005 | 23 | S | P4 | Reporting & Analytics | - | - | - |
| CAP-RPT-006 | 23 | C | P4 | Reporting & Analytics | - | - | - |
| CAP-RPT-007 | 23 | M | P4 | Reporting & Analytics | - | - | - |
| CAP-INT-001 | 24 | M | P3 | Integration & Adapters | - | - | - |
| CAP-INT-002 | 24 | M | P3 | Integration & Adapters | - | - | - |
| CAP-INT-003 | 24 | M | P3 | Integration & Adapters | - | - | - |
| CAP-INT-004 | 24 | M | P3 | Integration & Adapters | - | - | - |
| CAP-INT-005 | 24 | M | P3 | Integration & Adapters | - | - | - |
| CAP-INT-006 | 24 | M | P3 | Integration & Adapters | - | - | - |
| CAP-INT-007 | 24 | S | P3 | Integration & Adapters | - | - | - |
| CAP-INT-008 | 24 | M | P3 | Integration & Adapters | - | - | - |

## Requirement text

Full text of every requirement, for readers without the specifications to hand.

### Capability 1 - Ingestion & Import (ING)

- **CAP-ING-001** (M) Accept FHIR ePI Bundles in JSON and XML via authenticated API and UI upload.
- **CAP-ING-002** (M) Accept source documents (PDF, HTML, Word/DOCX) for downstream conversion/authoring.
- **CAP-ING-003** (M) Accept and classify agency **artwork PDFs**, storing them as managed assets linked to a label version; never parse them as ePI content.
- **CAP-ING-004** (M) Perform structural pre-validation on ingest (well-formed, parseable, required anchors present) before acceptance; delegate deep validation to #11.
- **CAP-ING-005** (M) Capture provenance for every ingested item: source, submitter/actor, timestamp, channel, original filename, and content hash.
- **CAP-ING-006** (M) Retain the original submitted artifact (binary) immutably, linked to the created/staged record.
- **CAP-ING-007** (M) Resolve product/label identity on ingest against master data (#5); flag unresolved identifiers.
- **CAP-ING-008** (M) Quarantine items failing pre-validation into a rejection/exception state with actionable, itemised errors.
- **CAP-ING-009** (S) Detect duplicates by business identifier + version + content hash; apply configurable dedupe policy.
- **CAP-ING-010** (M) Support batch/bulk submission with a batch context, per-item status, and resumability (used by #4).
- **CAP-ING-011** (S) Expose an idempotent ingestion API (safe re-submission without duplicate side effects).
- **CAP-ING-012** (M) Emit ingestion lifecycle events (received, staged, rejected) to the event backbone (#20).
- **CAP-ING-013** (M) Enforce ingestion authorisation and affiliate/market scoping via #17; record to audit (#19).
- **CAP-ING-014** (S) Support configurable per-channel/source ingestion profiles (accepted types, validation strictness) via #21.

### Capability 2 - Structured Content Model (SCM)

- **CAP-SCM-001** (M) Represent an ePI as a FHIR document `Bundle` anchored by a `Composition` with typed, coded sections.
- **CAP-SCM-002** (M) Support the ePI product-data resource graph by reference (e.g. `MedicinalProductDefinition`, `PackagedProductDefinition`, `ManufacturedItemDefinition`, `AdministrableProductDefinition`, `Ingredient`, `SubstanceDefinition`, `ClinicalUseDefinition`, `RegulatedAuthorization`, `Organization`).
- **CAP-SCM-003** (M) Support both structured (coded, referenceable) and narrative section content.
- **CAP-SCM-004** (M) Provide a **content-reuse / single-sourcing** mechanism: referenceable shared content units (e.g. class labelling, reusable blocks) so one source updates every referencing label.
- **CAP-SCM-005** (M) Support **cross-references** within a document and across documents, with referential integrity and resolution.
- **CAP-SCM-006** (M) Provide a governed **extension** mechanism for market/regulator-specific data without forking the core model.
- **CAP-SCM-007** (M) Define **canonical identifier and versioning semantics** for documents, sections, and reusable units (stable IDs across languages/markets/versions).
- **CAP-SCM-008** (M) Bind section types and coded elements to terminology/value sets from #6.
- **CAP-SCM-009** (M) Expose the content model/schema (profiles, section taxonomy, extensions) as a service to authoring (#3), validation (#11), and rendering (#13).
- **CAP-SCM-010** (M) Preserve full fidelity round-trip: a conformant ePI can be represented and re-serialised without content loss.
- **CAP-SCM-011** (M) Support association of a label document to its product/packaging identity (the product <-> packaging <-> label association model, with #5).
- **CAP-SCM-012** (M) Distinguish canonical content from generated representations (HTML/PDF) and from linked artwork assets.

### Capability 3 - Template & Label-Type Management (TPL)

- **CAP-TPL-001** (M) Maintain a versioned library of templates keyed by label type, market/regulator, and product type.
- **CAP-TPL-002** (M) A template defines section structure, mandatory/optional flags, ordering, section metadata, terminology bindings, and default/boilerplate content.
- **CAP-TPL-003** (M) Each template targets a conformance profile from #10 (the structure it must produce).
- **CAP-TPL-004** (M) Instantiate a new label from a template, producing a conformant, pre-scaffolded draft handed to #7.
- **CAP-TPL-005** (M) Provide template-driven **guided authoring**: field-level guidance, allowed value sets (#6), and section prompts that shield authors from raw FHIR.
- **CAP-TPL-006** (M) Derive **variant templates** from a core template via inheritance with controlled add/override (market/language variants).
- **CAP-TPL-007** (M) Version templates with effective dates; record which template (and version) each label was instantiated from.
- **CAP-TPL-008** (M) Support template lifecycle (draft, in-review, approved, retired) with approval via #16 and access via #17 (template-owner role).
- **CAP-TPL-009** (M) On template change, list impacted labels (those created from prior versions) for optional re-alignment; never mutate existing labels automatically.
- **CAP-TPL-010** (S) Validate a template's own structural correctness against its target profile before it can be approved.
- **CAP-TPL-011** (S) Support boilerplate/default content as reusable units (#2) so shared text is single-sourced across templates.
- **CAP-TPL-012** (S) Allow configurable policy on instantiation from non-approved templates (block by default).

### Capability 4 - Data Migration & Legacy Onboarding (MIG)

- **CAP-MIG-001** (M) Provide a bulk import pipeline for legacy formats (PDF, SPL XML, Word/HTML) at scale, built on the #1 batch path.
- **CAP-MIG-002** (M) Automated extraction/conversion of legacy content into the structured model (#2), aligned to a target template/profile (#3/#10).
- **CAP-MIG-003** (M) Transform SPL (HL7 v3) sources into FHIR ePI via a maintained mapping.
- **CAP-MIG-004** (M) Produce a **reconciliation report** per item and per batch: converted vs source coverage, confidence score, and identified gaps.
- **CAP-MIG-005** (M) Route low-confidence or failed items to a **remediation workflow** (human-in-the-loop); never silently drop items.
- **CAP-MIG-006** (M) Configurable target lifecycle state for migrated items (e.g. imported-historical vs active-approved), defaulting to non-approved.
- **CAP-MIG-007** (M) Capture provenance linking each migrated label to its legacy source artifact (retained immutably).
- **CAP-MIG-008** (M) Batch runs are idempotent, resumable, and fully auditable; dropped/failed/deduped items are logged, not hidden.
- **CAP-MIG-009** (S) Deduplicate against already-loaded content by identifier/version/hash.
- **CAP-MIG-010** (S) Provide throughput/scale controls (throttling, parallelism) and progress reporting for large backfills.
- **CAP-MIG-011** (S) Support dry-run/simulation mode producing reconciliation output without loading.
- **CAP-MIG-012** (S) Emit migration job and item events (#20) for monitoring and reporting (#23).

### Capability 5 - Master Data & Identifiers (MDM)

- **CAP-MDM-001** (M) Reference ISO IDMP identifiers for substance, medicinal product, packaged product, and organisation.
- **CAP-MDM-002** (M) Integrate with EMA SPOR services (SMS substance, PMS product, RMS referential, OMS organisation) and/or an internal MDM/RIM platform as the system(s) of record.
- **CAP-MDM-003** (M) Maintain the product <-> packaging <-> label association model linking each label document to the regulated product/packaging it describes.
- **CAP-MDM-004** (M) Resolve and validate identifiers used in content, flagging unresolved or ambiguous references.
- **CAP-MDM-005** (M) Hold a governed local replica/cache of referenced master data with synchronisation and version tracking.
- **CAP-MDM-006** (M) Detect upstream master-data changes to referenced entities and emit impact events to #8 (and #20).
- **CAP-MDM-007** (S) Define source-of-truth precedence when multiple sources exist (e.g. SPOR vs internal MDM).
- **CAP-MDM-008** (M) Expose an identifier resolution and association API to #1, #2, #3.
- **CAP-MDM-009** (S) Record provenance and effective dates for referenced master-data snapshots used by an approved label.
- **CAP-MDM-010** (S) Handle stale-cache and unavailability gracefully (last-known-good, staleness flags), without blocking authoring where policy allows.

### Capability 6 - Terminology & Code System Management (TRM)

- **CAP-TRM-001** (M) Host and manage code systems, value sets, and concept maps (FHIR `CodeSystem`, `ValueSet`, `ConceptMap`).
- **CAP-TRM-002** (M) Provide terminology operations: validate-code, expand, lookup, and translate.
- **CAP-TRM-003** (M) Manage licensed terminologies (e.g. SNOMED CT, MedDRA) with license/access controls and usage constraints.
- **CAP-TRM-004** (M) Version code systems and value sets with effective dates and retirement/deprecation handling.
- **CAP-TRM-005** (M) Publish terminology **bindings** (element -> value set + binding strength) consumed by #2, #3, #11.
- **CAP-TRM-006** (S) Provide concept maps for cross-scheme mapping feeding regulatory mapping (#10).
- **CAP-TRM-007** (M) Import terminology releases on a schedule and track source versions (via #24).
- **CAP-TRM-008** (S) Support value-set expansion suitable for interactive authoring pickers (performance-bounded).
- **CAP-TRM-009** (M) Validate a code against its bound value set and the version effective for the content's date.
- **CAP-TRM-010** (S) Expose terminology capability via a FHIR terminology service interface.

### Capability 7 - Lifecycle & Version Management (LCM)

- **CAP-LCM-001** (M) Provide a configurable lifecycle state model with explicitly allowed transitions.
- **CAP-LCM-002** (M) Version every label as immutable snapshots with a version lineage.
- **CAP-LCM-003** (M) Maintain **internal lifecycle state** separately from **per-market regulatory-approval state**.
- **CAP-LCM-004** (M) Support effective dating: when an approved version becomes effective.
- **CAP-LCM-005** (M) Support supersession (a new approved version supersedes a prior) and withdrawal.
- **CAP-LCM-006** (M) Reconstruct the full content and metadata of any historical version.
- **CAP-LCM-007** (M) Enforce transitions through workflow (#16) and permissions/segregation of duties (#17).
- **CAP-LCM-008** (S) Manage concurrent editing via locking and/or branching with defined merge/conflict rules.
- **CAP-LCM-009** (M) Link each version to the change/variation (#8) and, where relevant, submission that produced it.
- **CAP-LCM-010** (M) Emit lifecycle/state-change events (#20).
- **CAP-LCM-011** (M) Pin the content snapshot (including reusable-unit versions per policy) at approval.

### Capability 8 - Change Management & Impact Analysis (CHG)

- **CAP-CHG-001** (M) Intake source changes: CCDS/CDS updates and clinical safety signals, manually and via integration (#24).
- **CAP-CHG-002** (M) Compute downstream **impact sets**: affected products, markets, affiliates, labels, and sections, using the content/reuse graph (#2) and product associations (#5).
- **CAP-CHG-003** (M) Provide change/diff tracking (redline) across label versions (#7).
- **CAP-CHG-004** (M) Generate and track change/variation tasks per affected label.
- **CAP-CHG-005** (M) Link a change to regulatory submissions/variations (#24) and to the triggering signal/source.
- **CAP-CHG-006** (M) Record **CDS-origin traceability**: associate each affected section with the source change, feeding #12's no-origin checks.
- **CAP-CHG-007** (M) Provide a propagation-completeness dashboard (which affected labels are done/outstanding).
- **CAP-CHG-008** (S) Consume master-data change impacts from #5 and fold them into impact analysis.
- **CAP-CHG-009** (M) Prevent closure while any affected label remains untracked/outstanding.
- **CAP-CHG-010** (S) Emit change lifecycle events (#20) and feed reporting (#23).

### Capability 9 - Localisation, Multi-region & Translation Management (LOC)

- **CAP-LOC-001** (M) Model market variants (country x language x regulator) linked to a core/source label.
- **CAP-LOC-002** (M) Provide in-system translation management: workflow, translation memory, glossary/termbase, and linguistic review.
- **CAP-LOC-003** (M) Perform structured, section-level (in-context) translation preserving the content structure (#2).
- **CAP-LOC-004** (S) Leverage content reuse (#2) and templates (#3) so shared content is translated once and reused.
- **CAP-LOC-005** (M) Track translation status per variant and per section; flag translations as **stale** when the source changes (with #8).
- **CAP-LOC-006** (M) Support at least the EU's official languages and required scripts/directionality.
- **CAP-LOC-007** (M) Enforce affiliate/market scoping (#17) and segregation of translator vs approver roles.
- **CAP-LOC-008** (C) Provide an optional integration hook for an external TMS (via #24), disabled by default.
- **CAP-LOC-009** (S) Emit localisation/translation events (#20) and feed reporting (#23).

### Capability 10 - Regulatory Mapping & Conformance Profiles (REG)

- **CAP-REG-001** (M) Maintain conformance profiles per regulator/market (FHIR `StructureDefinition`/IG level): mandatory sections, cardinalities, terminology bindings.
- **CAP-REG-002** (M) Maintain mappings between schemes (FHIR ePI <-> SPL; EU <-> national) via `ConceptMap`/`StructureMap`.
- **CAP-REG-003** (M) Manage national **extensions** layered over the core ePI model, from an approved registry.
- **CAP-REG-004** (M) Version profiles and mappings with effective dates (to absorb standards flux).
- **CAP-REG-005** (M) Publish profiles/mappings for consumption by #3 (templates), #11 (validation), #12 (completeness), #4 (migration transforms).
- **CAP-REG-006** (M) Add a new market/regulator by configuration (#21) without a code release.
- **CAP-REG-007** (M) Track the conformance target per market (which IG/profile version is authoritative).
- **CAP-REG-008** (S) Validate profile internal consistency before activation.

### Capability 11 - Validation & Quality (VAL)

- **CAP-VAL-001** (M) Validate FHIR resource conformance against the applicable active profile(s) from #10.
- **CAP-VAL-002** (M) Validate terminology bindings from #6, honouring binding strength (required/extensible/preferred).
- **CAP-VAL-003** (M) Validate structural well-formedness and reference integrity (#2), including no dangling references in approval candidates.
- **CAP-VAL-004** (M) Run validation at defined gates: ingest (#1), save/author (#3), pre-approval (#7), pre-publish (#14).
- **CAP-VAL-005** (M) Produce structured, actionable issues with severity and precise location (section/element).
- **CAP-VAL-006** (S) Support batch validation for migration (#4).
- **CAP-VAL-007** (S) Configurable rule severity/strictness per gate and per market (#21).
- **CAP-VAL-008** (M) Expose validation via API and as interactive authoring feedback.

### Capability 12 - Compliance & Completeness Checking (CMP)

- **CAP-CMP-001** (M) Check completeness against the market template (#3) and active profile (#10): required sections present, correctly ordered, and populated.
- **CAP-CMP-002** (M) Detect missing mandatory sections and unexpected/extra sections.
- **CAP-CMP-003** (M) Detect section-level structural/compliance issues beyond FHIR validity.
- **CAP-CMP-004** (M) Run configurable business/compliance rule sets via a rule engine (config-as-data, #21), per market.
- **CAP-CMP-005** (M) Perform the **CDS-origin check**: flag every label section with no approved source origin, using #8's section-to-source lineage.
- **CAP-CMP-006** (M) Produce a compliance report per label/market with rationale and traceability for each finding.
- **CAP-CMP-007** (M) Gate approval/publish on compliance outcome (blocking severities configurable).
- **CAP-CMP-008** (S) Version compliance rule sets with effective dates and audit.

### Capability 13 - Rendering & Transformation (RND)

- **CAP-RND-001** (M) Render FHIR ePI to accessible, structured HTML.
- **CAP-RND-002** (M) Render FHIR ePI to PDF for regulatory/electronic distribution (the rendered-PDF lineage, distinct from artwork PDF).
- **CAP-RND-003** (M) Apply market/QRD styling templates and branding via versioned render templates.
- **CAP-RND-004** (M) Produce interactive author previews (draft, watermarked) and official renders (approved).
- **CAP-RND-005** (S) Meet accessibility conformance (WCAG) for HTML output; consider PDF/UA and PDF/A for distribution/archival.
- **CAP-RND-006** (S) Perform scheme transformations where required (e.g. FHIR ePI -> SPL), using #10 mappings.
- **CAP-RND-007** (M) Deterministic, reproducible renders (same input+template version -> same output).
- **CAP-RND-008** (S) Bounded, observable rendering pipeline; asynchronous for heavy renders.
- **CAP-RND-009** (M) Link rendered outputs to the exact label version and render-template version.

### Capability 14 - Publishing & Distribution (PUB)

- **CAP-PUB-001** (M) Publish approved ePI (FHIR) and rendered representations (HTML/PDF from #13) to configured channels.
- **CAP-PUB-002** (M) Support channel types: national regulator databases/portals, company ePI repository/API, and syndication feeds.
- **CAP-PUB-003** (M) Support scheduled/effective-dated publication and embargo, aligned to #7 effective dates.
- **CAP-PUB-004** (M) Provide a clean published-content API/feed for downstream consumers, including the future consumer-delivery system.
- **CAP-PUB-005** (M) Support unpublish/withdrawal and republication, propagating to channels.
- **CAP-PUB-006** (M) Track publication status and history per label version, market, and channel.
- **CAP-PUB-007** (S) Idempotent, retryable channel delivery with receipt confirmation where the channel supports it.
- **CAP-PUB-008** (M) Route publication per market to the correct channel set via configuration (#21).
- **CAP-PUB-009** (S) Emit publication events (#20) and feed reporting (#23).

### Capability 15 - Search, Access & Retrieval (SCH)

- **CAP-SCH-001** (M) Provide a FHIR RESTful API with search parameters (product, market, language, status, identifier, effective date).
- **CAP-SCH-002** (M) Retrieve a specific version and the current-approved version of a label per market/language.
- **CAP-SCH-003** (M) Provide full-text and structured search across content and metadata.
- **CAP-SCH-004** (M) Scope all results by caller permissions/attributes (#17); never leak out-of-scope content.
- **CAP-SCH-005** (M) Serve authorised downstream consumers read access to approved content (aligned to #14).
- **CAP-SCH-006** (S) Provide performance-bounded queries with pagination and result limits.
- **CAP-SCH-007** (S) Return links to available representations (FHIR/HTML/PDF) for a retrieved label.
- **CAP-SCH-008** (S) Expose an auditor/inspection-oriented retrieval path (with #19).

### Capability 16 - Workflow & Approvals (WFL)

- **CAP-WFL-001** (M) Configurable multi-step review/approval workflows per market and label type (config-as-data, #21).
- **CAP-WFL-002** (M) Task assignment, reassignment, escalation, and due dates.
- **CAP-WFL-003** (M) Invoke electronic signature at approval gates (#19).
- **CAP-WFL-004** (M) Enforce segregation of duties via #17 (e.g. author cannot approve own content).
- **CAP-WFL-005** (M) Drive lifecycle transitions in #7 on step completion.
- **CAP-WFL-006** (S) Support sequential and parallel review paths and conditional routing.
- **CAP-WFL-007** (M) Notify participants of assignments, due dates, and outcomes (#20).
- **CAP-WFL-008** (M) Fully audit all workflow actions (#19).

### Capability 17 - Identity, Access Control & Permissions - RBAC/ABAC (IAM)

- **CAP-IAM-001** (M) Authenticate via the enterprise IdP (OIDC/SAML) with SSO/federation and MFA (delegated to IdP).
- **CAP-IAM-002** (M) Enforce combined RBAC + ABAC authorization on every action and API.
- **CAP-IAM-003** (M) Scope permissions across affiliate/organisation, region/market, product & label scope, lifecycle state, and template.
- **CAP-IAM-004** (M) Provide functional roles: author, reviewer, approver, publisher, template-owner, administrator, and auditor (read-only).
- **CAP-IAM-005** (M) Support permission granting with delegated, affiliate-scoped administration (a delegated admin cannot exceed their own scope).
- **CAP-IAM-006** (M) Enforce segregation of duties (e.g. author != approver) across workflows (#16).
- **CAP-IAM-007** (M) Enforce multi-tenant isolation of affiliate data.
- **CAP-IAM-008** (M) Provide central policy enforcement consumed uniformly by all capabilities.
- **CAP-IAM-009** (M) Record all access-control decisions and administration changes to audit (#19).

### Capability 18 - Security (SEC)

- **CAP-SEC-001** (M) Encrypt data in transit (TLS) and at rest.
- **CAP-SEC-002** (M) Manage secrets and cryptographic keys via a managed secrets/key store; no plaintext secrets.
- **CAP-SEC-003** (M) Apply network security (segmentation, gateway/WAF) and application security (input validation, OWASP Top 10 controls).
- **CAP-SEC-004** (M) Vulnerability and patch management, including dependency/container scanning.
- **CAP-SEC-005** (S) Follow a secure SDLC with security testing (SAST/DAST) integrated into CI/CD.
- **CAP-SEC-006** (S) Apply data-protection controls appropriate to the data held (access data, limited PII).
- **CAP-SEC-007** (S) Support security monitoring/alerting integrated with observability (#23) and audit (#19).

### Capability 19 - Audit Trail, e-Signature & Inspection Support (AUD)

- **CAP-AUD-001** (M) Capture a comprehensive audit trail of all GxP-relevant actions (who, what, when, why, before/after values).
- **CAP-AUD-002** (M) Store audit records tamper-evidently/immutably (append-only).
- **CAP-AUD-003** (M) Provide electronic signatures per 21 CFR Part 11 & EU Annex 11: signing events, signature meaning/manifest, and binding to the signed record and signer identity.
- **CAP-AUD-004** (M) Reconstruct the full history of any record.
- **CAP-AUD-005** (M) Provide auditor/investigator access via a read-only inspection role and audit mode (#17).
- **CAP-AUD-006** (M) Provide audit-trail search, filtering, and visibility for periodic audit-trail review and regulatory inspections.
- **CAP-AUD-007** (M) Export and report audit content for inspection support.
- **CAP-AUD-008** (M) Receive audit events from every capability via the event backbone (#20) and direct writes.
- **CAP-AUD-009** (M) Apply retention and legal hold to audit records in coordination with #22.

### Capability 20 - Notifications, Events & Subscriptions (EVT)

- **CAP-EVT-001** (M) Provide a publish/subscribe event backbone for inter-capability communication.
- **CAP-EVT-002** (M) Support FHIR Subscription for content/resource change notifications.
- **CAP-EVT-003** (M) Deliver user notifications (in-app, email) and external webhooks.
- **CAP-EVT-004** (M) Alert affiliates/consumers when a change (#8) or new version (#7/#14) affects their scope.
- **CAP-EVT-005** (M) Provide at-least-once delivery with retries and dead-letter handling; ordering where required.
- **CAP-EVT-006** (S) Maintain an event catalogue/schema registry (config-as-data, #21).
- **CAP-EVT-007** (M) Scope notifications by permission/attribute (#17).
- **CAP-EVT-008** (M) Route all events to the audit sink (#19) where GxP-relevant.

### Capability 21 - Configuration & Business-Rule Management (CFG)

- **CAP-CFG-001** (M) Externalise configuration for markets/regulators, lifecycle state models, workflows, validation/compliance rules, terminology bindings, publishing routing, and event schemas.
- **CAP-CFG-002** (M) Provide a business-rule engine (rules as data) consumed by #11, #12, and lifecycle (#7).
- **CAP-CFG-003** (M) Version configuration and rules with effective dates and full audit (#19).
- **CAP-CFG-004** (M) Onboard a new market/regulator via configuration without a code release.
- **CAP-CFG-005** (M) Govern configuration changes with approval (via #16) and access control (#17).
- **CAP-CFG-006** (M) Validate configuration consistency before activation.
- **CAP-CFG-007** (M) Support environment-aware configuration (dev/test/prod) with controlled promotion (GxP/CSV).

### Capability 22 - Records Retention & Archival (RET)

- **CAP-RET-001** (M) Define and apply retention schedules per record type and market (config-as-data, #21).
- **CAP-RET-002** (M) Archive content, versions, rendered outputs, and audit records for long-term retention.
- **CAP-RET-003** (M) Apply legal hold that overrides scheduled disposition.
- **CAP-RET-004** (M) Perform defensible disposition/destruction at end of retention, with approval (#16) and audit (#19).
- **CAP-RET-005** (M) Ensure archived records remain reconstructable and readable (format longevity).
- **CAP-RET-006** (M) Coordinate audit-record retention with #19.
- **CAP-RET-007** (S) Support immutable/WORM archival where required.

### Capability 23 - Reporting & Analytics (RPT)

- **CAP-RPT-001** (M) Provide operational dashboards: throughput, backlog, cycle times, publication status/coverage.
- **CAP-RPT-002** (M) Provide regulatory/compliance reports: completeness (#12), change propagation (#8), overdue variations, CDS-origin gaps.
- **CAP-RPT-003** (S) Provide change-impact and propagation reporting sourced from #8.
- **CAP-RPT-004** (S) Provide publication/coverage reporting per market and product (from #14).
- **CAP-RPT-005** (S) Support ad-hoc query/report building and export.
- **CAP-RPT-006** (C) Distribute scheduled reports to authorised recipients.
- **CAP-RPT-007** (M) Respect access scope (#17) in all reporting.

### Capability 24 - External Integration (INT)

- **CAP-INT-001** (M) Provide an adapter framework for external systems (RIM/submission, DMS, IdP, SPOR/IDMP, artwork hand-off, notification, national channels).
- **CAP-INT-002** (M) Inbound integrations: IdP/federation (#17), SPOR/IDMP master data (#5), source changes/signals (#8), artwork PDFs (#1).
- **CAP-INT-003** (M) Outbound integrations: submission/variation linkage (#8), publication to national/company channels (#14), notifications/webhooks (#20).
- **CAP-INT-004** (M) Support standard integration patterns (synchronous API/FHIR, events, batch/file) with transformation/mediation.
- **CAP-INT-005** (M) Provide resilience (retry, circuit-break, idempotency) and integration monitoring.
- **CAP-INT-006** (M) Manage connection configuration and credentials securely (#18) with routing config (#21).
- **CAP-INT-007** (S) Expose published content to the future consumer-delivery system as an integration consumer (option A).
- **CAP-INT-008** (M) Audit integration exchanges where GxP-relevant (#19).
