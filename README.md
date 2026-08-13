# ePI Platform - FHIR electronic Product Information Enterprise System

An enterprise system for ingesting, authoring, validating, managing the full lifecycle of, and
publishing **FHIR, PDF, and HTML** representations of electronic Product Information (ePI) labels
for regulated pharmaceuticals - across multiple countries, regulators, and local affiliates.

The platform is FHIR-native, standards-first (HL7 Global Core ePI IG, EU EMA/EMRN ePI, US FDA SPL,
ISO IDMP / EMA SPOR), event-driven, config-as-data extensible, and built to be validatable under
GxP / 21 CFR Part 11 / EU Annex 11 / GAMP 5.

> **Status:** Specification and architecture draft. This repository currently holds the solution
> specification, capability specifications, technical architecture, and a runnable local
> development stack. Application services are scaffolded and under active design.

## Repository structure

| Path | Contents |
|---|---|
| `specs/` | The **what** - solution overview (D1), the deliverables definition, and per-capability specifications (D2.1-D2.6) |
| `design/` | The **how** - technical architecture (D3), architecture decision records, and diagrams |
| `deploy/` | Deployment assets - the Docker Compose dev stack, Kubernetes manifests/Helm, and IaC (OpenTofu) |
| `src/` | Backend services and shared libraries (.NET) |
| `apps/` | Frontend applications (authoring and review UI) |
| `policies/` | Authorization policies (OPA/Rego) and business rules (DMN) - config-as-data |
| `profiles/` | FHIR conformance artifacts - StructureDefinitions, ValueSets, ImplementationGuide packages |
| `config/` | Config-as-data - market/regulator definitions, lifecycle state models, terminology bindings |
| `tests/` | Test assets - sample FHIR bundles, conformance fixtures, end-to-end scenarios |
| `tools/` | Developer tooling and scripts |
| `docs/` | Supporting docs, generated exports, and source material |
| `.github/` | CI workflows and repository templates |

## Documentation map

Read in this order:

1. `specs/deliverables-definition.md` - the deliverable set and the capability catalogue (24 capabilities)
2. `specs/D1-solution-overview.md` - scope, domain primer, capability map, guiding architecture
3. `specs/capabilities/` - D2.1-D2.6, one document per capability domain group
4. `design/D3-technical-architecture.md` - components, data, APIs, security, deployment, tech stack, ADRs

## Quick start (local development stack)

A single-host, all-open-source backing stack (HAPI FHIR, Snowstorm, PostgreSQL, MinIO with
object-lock/WORM, Kafka, Keycloak, OPA, Gotenberg, OpenSearch) runs via Docker Compose:

```bash
cd deploy/docker-compose
cp .env.example .env
docker compose up -d
```

See `deploy/docker-compose/README.md` for service URLs, profiles, and the path to Kubernetes.

## Technology posture

Open-source and self-hostable by default; **Docker-first for development, Kubernetes for
test/production**. Every component ships as a maintained container image. A managed **Azure**
stack is a supported future target (the same images and portable abstractions lift onto AKS +
managed services). See `design/D3-technical-architecture.md` Section 12.

## Standards

HL7 FHIR (R5) + Global Core ePI IG; EMA/EMRN ePI + EU QRD; FDA SPL; ISO IDMP + EMA SPOR;
SNOMED CT, EDQM, UCUM, MedDRA, LOINC, ISO 639/3166; 21 CFR Part 11, EU Annex 11, GAMP 5.

## Contributing

See `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md`. Security disclosures: `SECURITY.md`.

## License

Licensed under the **Apache License, Version 2.0**. See `LICENSE` and `NOTICE`.
