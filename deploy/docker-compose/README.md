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

## The demonstration realm (identity, and who can sign)

Keycloak imports `init/keycloak-epi-realm.json` at start-up, so the realm, its clients and
its people are reproducible rather than clicked together. Import is skipped if the realm
already exists; to re-import after editing, delete the realm in the admin console (or via
the admin API) and restart the container.

Three fictional people, enough to demonstrate segregation of duties. **All passwords are
`Demo-Passw0rd!`** and every one of these is a development default.

| User | Name | Role | Affiliate | Markets |
|---|---|---|---|---|
| `user-anna` | Anna Novak | author | uk-affiliate | GB |
| `user-ben` | Ben Okafor | approver | uk-affiliate | GB |
| `user-rae` | Rae Lindqvist | regulatory | uk-affiliate | GB, EU |

Anna authors and submits; Ben approves, because the author of a version may not approve it.
Rae holds EU as well as GB, so the same content can hold different regulatory-approval state
in two markets (ADR-005).

Two clients: `epi-api` is the resource server and never obtains tokens, and `epi-signing` is
a public client with direct access grants - used both to sign a person in and by the platform
itself to re-check a password at a signing gate (ADR-020 decision 1).

The realm sets a password policy (`length(12) and notUsername and notEmail and
passwordHistory(3)`). ADR-020 names password ageing and complexity as met by configuration
rather than by the platform; this is that configuration, set so the claim is demonstrable
rather than asserted.

To get a token:

```
curl -s -X POST http://localhost:8081/realms/epi/protocol/openid-connect/token   -d client_id=epi-signing -d grant_type=password   -d username=user-anna -d "password=Demo-Passw0rd!"
```

The access token carries `affiliates`, `markets` and `roles`, which is what the platform
reads to scope every operation, and `aud=epi-api` so the API accepts it.

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
