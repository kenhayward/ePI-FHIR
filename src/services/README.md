# src/services/

One project per domain service. Placeholder until implementation begins.

Each service should include: its API contract (OpenAPI / FHIR CapabilityStatement / AsyncAPI as
appropriate), unit and contract tests, a Dockerfile, and a Helm chart reference under
`deploy/kubernetes/`. Follow the boundaries in D3 Section 2 - services talk via APIs and the event
backbone; no service reaches another's datastore directly.
