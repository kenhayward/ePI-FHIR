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
| ePI API | The platform itself | http://localhost:8080 | bearer token from Keycloak |
| Authoring surface | The web app (ADR-037) | http://localhost:5173 | sign in as one of the users below |

\* observability profile  ** gateway profile  *** messaging profile

**The authoring surface is served on 5173** because that is a redirect URI the
`epi-authoring-ui` client already permits, and a port the identity provider does not permit is a
surface that cannot sign in. Its other permitted port, 8000, is Kong's under the gateway profile.
The cost is that `npm run dev` and the container cannot both run - two things nobody runs at once.
Set `EPI_UI_PORT` to move it, and add the new address to the client in
`init/keycloak-epi-realm.json` first.

Where the surface is pointed is `config/ui/authoring.json`, mounted rather than baked into the
image (ADR-049). **The addresses in it are the browser's, not the container network's:** a browser
resolves `localhost` and cannot reach `epi-api` or `keycloak` whatever the container serving the
files can. If you change `EPI_API_PORT`, `KEYCLOAK_PORT` or `EPI_UI_PORT`, change that file to
match - nothing can check it for you, because the browser is outside this network.

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

Four fictional people, enough to demonstrate segregation of duties. **All passwords are
`Demo-Passw0rd!`** and every one of these is a development default.

| User | Name | Role | Affiliate | Markets |
|---|---|---|---|---|
| `user-anna` | Anna Novak | author | uk-affiliate | GB |
| `user-ben` | Ben Okafor | approver | uk-affiliate | GB |
| `user-rae` | Rae Lindqvist | regulatory | uk-affiliate | GB, EU |
| `user-ops` | Omar Silva | platform-operator | uk-affiliate | GB |

Anna authors and submits; Ben approves, because the author of a version may not approve it.
Rae holds EU as well as GB, so the same content can hold different regulatory-approval state
in two markets (ADR-005). Omar operates the platform rather than authoring for it: the
reconciliation report is a platform-wide action, which no content role grants.

**Keycloak imports the realm only if it does not already exist.** A volume older than a user,
role or client added here therefore has none of them, and the symptom is never "the realm is out
of date" - it is a specific thing failing for a reason nobody attributes to configuration. It has
happened twice: `user-ops` missing left the reconciliation report answering 403 to the only
identity permitted to run it, and `epi-authoring-ui` missing left the authoring surface bouncing
off the identity provider with "Client not found", which reads like a bug in the surface.

```bash
python tools/verify-realm.py
```

**The same shape catches OPA.** It loads `policies/` at start-up, so a role added to
`policies/data/roles.json` after the container started is not in the running decision point - and
the symptom is a 403 with a correct-looking token. `docker compose restart opa` after changing
anything under `policies/`. This cost a diagnosis: the reconciliation report refused `user-ops`
whose token carried `platform-operator`, because the decision point had never heard of the role.

That says what the running realm lacks and changes nothing. To take a realm change into an
existing stack, delete the `epi` realm from the admin console and restart Keycloak, or start from
a fresh volume - but note first that **recreating the realm mints new subject identifiers**, and
the platform's audit records, signatures and pinned contexts are attributed to the old ones.
Nothing reconnects them. On a demonstration stack that is usually acceptable; it should be a
decision rather than a discovery.

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

## The seeded Keycloak realm

`init/keycloak-epi-realm.json` is imported by Keycloak on start-up, and it is **the one
configuration file in this repository that carries no `_comment` keys**. Keycloak's
`ClientRepresentation` rejects unknown fields rather than ignoring them, so an explanatory key
makes the whole realm import fail - which stops Keycloak, and with it the API and HAPI FHIR that
wait on it. `tools/verify-foreign-config.py` refuses one in CI. The reasoning that would have
gone inline lives here instead:

- **`epi-authoring-ui`** is the authoring surface, public and PKCE-only (ADR-039). It carries the
  same four protocol mappers `epi-signing` does, and it has to: without the audience mapper the API
  refuses its tokens as issued to somebody else, and without roles, affiliates and markets every
  authorization decision denies for want of anything to decide on. It shipped with none of them,
  and the symptom was a 401 from every call the surface made - found by opening the surface, not by
  reading (ADR-050). Its redirect URIs are `http://localhost:5173/*` and `http://localhost:8000/*`.
- **`epi-api`** is the audience. Confidential, and no flows enabled: it is what tokens are issued
  *for*, never what anybody signs in *through*.
- **`epi-signing`** backs the electronic-signature check, which verifies a signer's credentials
  at an approval gate (ADR-020). Direct access grants are enabled here and nowhere else, because
  re-authenticating at the moment of signing is the point of it.
- **`epi-authoring-ui`** is the authoring surface (ADR-039). Public, because a secret shipped to
  a browser is not a secret, and PKCE stands in for one. Standard flow only, S256 required, and
  direct access grants deliberately disabled: the surface must never handle a password.

## If PostgreSQL rejects the `epi` user

`POSTGRES_PASSWORD` is applied only when the data directory is first initialised. Running
`cp .env.example .env` over an `.env` whose password differed leaves a volume the new password
cannot open, and the failure cascades - Keycloak, the API and HAPI FHIR all exit on
`password authentication failed for user "epi"`, none of them mentioning the volume.

Realign the role rather than wiping two days of data:

```bash
docker compose exec postgres psql -U epi -d postgres -c "ALTER ROLE epi WITH PASSWORD 'devpassword';"
```

`docker compose down -v` also fixes it, by deleting everything.
