# deploy/kubernetes/

Kubernetes deployment assets (Helm charts and/or manifests) for test and production - the target
runtime per D3 Section 10.

Placeholder. When populated, expect:
- A chart per service (`src/services/*`) and per app (`apps/*`).
- Charts or references for the backing components (HAPI FHIR, Snowstorm + Elasticsearch, PostgreSQL,
  MinIO, Kafka, Keycloak, OPA, Gotenberg, OpenSearch, observability).
- Production hardening: secrets (Vault/sealed-secrets), TLS, NetworkPolicies, resource limits,
  HA replicas, persistent volumes.
- GitOps delivery via Argo CD / Tekton.

A quick starting point is to convert the dev compose (`../docker-compose`) with `kompose`, then
refine into Helm charts.
