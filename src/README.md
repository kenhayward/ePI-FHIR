# src/ - Backend services and libraries

Application services (primarily .NET / C#) and shared libraries implementing the capabilities in
`specs/`, per the logical decomposition in D3 Section 2.

## Contents
- `services/` - one project per domain service (see the mapping below). Several adopted components
  (HAPI FHIR, Snowstorm, Keycloak, Kafka) are off-the-shelf OSS and are configured under `deploy/`,
  not built here.

## Service-to-capability mapping (D3 Section 2.1)
| Service | Capabilities |
|---|---|
| authoring-template | 1, 3 (+ part of 2) |
| content-core (FHIR integration) | 2 |
| terminology (integration) | 6 |
| master-data | 5 |
| lifecycle-workflow | 7, 16 |
| change-impact | 8 |
| localisation-translation | 9 |
| regulatory-profiles | 10 |
| validation | 11 |
| compliance | 12 |
| rendering | 13 |
| publishing | 14 |
| search | 15 |
| migration | 4 |
| notification-events | 20 |
| configuration-rules | 21 |
| retention-archival | 22 |
| reporting | 23 |
| integration-adapters | 24 |
| iam | 17 |
| audit-esignature | 19 |

Start as a modular monolith or coarse-grained services, decomposable along these seams (D3 Section 1.4).
