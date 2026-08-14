# FHIR ePI - Local Development Stack (open-source)

A single-host Docker Compose stack that brings up the open-source backing services for the
FHIR ePI Enterprise System, per D3 Section 12. Every component here has a maintained Docker
image and deploys to Kubernetes unchanged (see "Path to Kubernetes" below). Azure managed
equivalents are a future target (D3 Section 12, Azure column).

> Development scaffold only. Dev-mode security (open ports, default credentials, TLS off).
> Do NOT use these settings beyond local development.

## Prerequisites
- Docker Engine + Docker Compose v2
- ~8 GB RAM free (Elasticsearch for Snowstorm and the JVM services are the heavy parts;
  raise Docker Desktop memory to 8-12 GB)
- On Linux, Elasticsearch/OpenSearch may need: `sudo sysctl -w vm.max_map_count=262144`

## Quick start
```bash
cp .env.example .env
docker compose up -d                          # core services
docker compose ps                             # check health
docker compose --profile observability up -d  # + OTel/Prometheus/Grafana
docker compose --profile gateway up -d        # + Kong API gateway
docker compose --profile messaging up -d      # + RabbitMQ
docker compose down                           # stop (add -v to remove volumes/data)
```

## Services and URLs

| Service | Role (capability) | URL / port | Default creds |
|---|---|---|---|
| HAPI FHIR | FHIR content core (2) | http://localhost:8090/fhir | - |
| Snowstorm | Terminology server (6) | http://localhost:8091/fhir | - |
| Elasticsearch | Snowstorm backing store | http://localhost:9200 | - |
| OpenSearch | Content search index (15) | http://localhost:9201 | - |
| MinIO (S3) | Object store + WORM (19, 22) | http://localhost:9000 (API), http://localhost:9001 (console) | epiadmin / devpassword123 |
| Kafka | Event backbone (20) | localhost:9092 | - |
| Keycloak | Identity / OIDC (17) | http://localhost:8081 | admin / admin |
| OPA | Policy decision point (17) | http://localhost:8181 | - |
| Gotenberg | HTML to PDF rendering (13) | http://localhost:3000 | - |
| PostgreSQL | Operational + HAPI + Keycloak DBs | localhost:5432 | epi / devpassword |
| Prometheus* | Metrics | http://localhost:9090 | - |
| Grafana* | Dashboards | http://localhost:3001 | admin / admin |
| Kong** | API gateway (8) | http://localhost:8000 (proxy), http://localhost:8001 (admin) | - |
| RabbitMQ*** | Messaging (optional, 20) | http://localhost:15672 | guest / guest |

\* observability profile  ** gateway profile  *** messaging profile

### If a port is already taken

Every published host port above is the default, and every one can be overridden from
`.env` - the variable names are listed there, commented out. Nothing else changes: the
ports inside the container network are fixed, so only your side of the mapping moves.

Corporate endpoint agents are the usual squatters, and they will not give a port up.
Zscaler's tunnel binds **9001**, which is MinIO's console by default, and the failure
arrives as `ports are not available ... bind: Only one usage of each socket address`.
To find the holder on Windows:

```
netstat -ano | findstr :9001
```

then `Get-Process -Id <pid>` in PowerShell to name it. Move our port rather than the
agent's - set `MINIO_CONSOLE_PORT=9101` in `.env` and bring the stack up again.

## WORM / object-lock (audit and retention)
The `minio-init` one-shot creates buckets with **object-lock (WORM)** enabled:
`epi-content`, `epi-artwork`, `epi-rendered`, `epi-audit`, `epi-archive`. The `epi-audit`
and `epi-archive` buckets carry a default COMPLIANCE retention (10 years here as an example).
This delivers the tamper-evident audit exports (cap 19) and long-term retention/legal-hold
(cap 22) locally, mirroring Azure Blob immutable in the future target (ADR-013).

## Notes on components
- **HAPI FHIR** is set to FHIR **R5** (the ePI IG baseline). Pin the exact IG/profile release in
  configuration (D2.3 cap 10).
- **Snowstorm** requires **Elasticsearch 8.x** (not OpenSearch); OpenSearch here serves the
  separate content-search index (cap 15). Load a SNOMED CT release into Snowstorm separately
  (licence required); MedDRA/EDGM value sets are loaded as FHIR ValueSets.
- **Keycloak** 26.x uses `KC_BOOTSTRAP_ADMIN_*` for the initial admin. Create the `epi` realm,
  clients, and role/attribute mappings to back IAM (cap 17).
- **Application services** (the .NET services we build - Authoring, Lifecycle/Workflow, Change,
  Compliance, Publishing, etc.) are not included here yet; add them as additional compose
  services (or a separate compose file) pointing at these backing services.

## Image pinning
Tags here favour readability; pin exact digests per environment in the compose/IaC before any
shared or qualified environment (GxP/CSV controlled release, D3 Section 10.3).

## Path to Kubernetes (target)
The same images run on Kubernetes:
1. Generate baseline manifests (e.g. `kompose convert`) or author Helm charts per service.
2. Replace dev shortcuts with production config: secrets (Vault/sealed-secrets), TLS,
   network policies, resource limits, HA replicas, persistent volumes.
3. Deliver via GitOps (Argo CD / Tekton), IaC via OpenTofu (D3 Section 10, ADR-014).
4. Optional future: lift onto Azure AKS + managed services (D3 Section 12, Azure column).

See D3 Section 10 (Deployment) and Section 12 (Technology stack) for the full picture.
