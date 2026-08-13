# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

An enterprise FHIR ePI (electronic Product Information) platform for regulated pharmaceutical
labelling. **It is currently specification + architecture + a dev stack - there is no application
code yet.** `src/`, `apps/`, `config/`, `profiles/`, `policies/rules/`, `deploy/kubernetes/`, and
`deploy/iac/` contain only READMEs describing what will go there. There is no build system and no
package manifest yet; do not invent one without being asked. When the first service does land, the
test project lands with it - before the implementation (see below).

The Markdown under `specs/` and `design/` is the product. Treat it as source code: it is the
version-controlled source of truth, and the `.docx` files under `docs/exports/` are generated
output (git-ignored).

## How work gets done here (non-negotiable)

**Test-driven development.** Write the test first, run it, and *see it fail* before writing a line
of implementation. The red run is evidence - capture it (a test-only commit, or the CI run URL) and
reference it in the pull request. Then make it pass with the smallest change that does so, then
refactor. Never write implementation ahead of a failing test, never weaken or delete a test to get
to green, and never report work as done on the strength of a test that has only ever passed. This
applies to Rego policies (`opa test`) and configuration rules as much as to service code - in a GxP
system the tests *are* part of the traceability from requirement to release.

**Everything goes through a pull request.** Branch from `main`, push, open a PR. Never commit to
`main` directly and never merge your own work: this is a public repository under branch protection,
and **every merge requires human approval from the maintainer**. Your job ends at "PR open, CI
green, ready for review" - say so plainly and stop there.

**CI on GitHub-hosted cloud runners is the authoritative gate.** Build and test results come from
[.github/workflows/ci.yml](.github/workflows/ci.yml) running on `ubuntu-latest`, not from a local
run. A local pass is a useful early signal and nothing more; do not describe a change as verified
until CI is green on the PR. Fix the workflow rather than working around it, and do not disable,
skip, or `continue-on-error` a failing check to get a PR green.

The PR template ([.github/pull_request_template.md](.github/pull_request_template.md)) encodes this:
traceability to a `CAP-<abbr>-NNN` requirement and/or an ADR, the red-then-green evidence, and the
checklist. A `tdd-guard` CI job fails any PR that changes production code under `src/` or `apps/`
without touching tests (escape hatch: the `no-tests-needed` label plus a justification).

## Commands

Local development stack (all open-source backing services, single host):

```bash
cd deploy/docker-compose && cp .env.example .env && docker compose up -d
```

Optional profiles: `--profile observability` (OTel/Prometheus/Grafana), `--profile gateway`
(Kong), `--profile messaging` (RabbitMQ). `docker compose down -v` wipes volumes.
Needs ~8 GB free RAM (Elasticsearch + JVM services). Service URLs, ports, and dev credentials are
tabulated in [deploy/docker-compose/README.md](deploy/docker-compose/README.md).

Regenerate the Word exports (requires `pandoc`; run from the repository root - use Git Bash on
Windows):

```bash
./tools/export-docx.sh
```

Policy checks - the same ones CI runs (write `*_test.rego` first; only
[policies/authz/example.rego](policies/authz/example.rego) exists today):

```bash
opa fmt --list --fail policies && opa check policies && opa test policies -v
```

Validate the compose stack the way CI does, before pushing:

```bash
cd deploy/docker-compose && cp .env.example .env && docker compose config -q
```

## Document architecture

Three deliverables, read in this order - each layer refines the previous and they must stay
consistent:

1. [specs/deliverables-definition.md](specs/deliverables-definition.md) - deliverable set and the 24-capability catalogue
2. [specs/D1-solution-overview.md](specs/D1-solution-overview.md) - scope, domain primer, capability map, principles, roadmap
3. `specs/capabilities/D2.1`-`D2.6` - one document per domain group, capabilities 1-24
4. [design/D3-technical-architecture.md](design/D3-technical-architecture.md) - components, data, APIs, security, deployment, tech stack, ADR-001...ADR-014

**The capability numbers 1-24 are stable identifiers across all documents.** Specs refer to other
capabilities as `#N` (e.g. "emit impact events to #8"). Requirement IDs are `CAP-<abbr>-NNN`
(e.g. `CAP-MDM-003`) with priority M/S/C. Each capability follows a fixed 10-section template
(purpose, actors/flows, requirements, data model, standards mapping, owned state, business rules,
interfaces, acceptance criteria, dependencies) - preserve it when editing.

**Separation of concerns between documents:** `specs/` states *what* and must stay
implementation-neutral; product and technology choices belong in `design/` only. Behaviour in D2
must not depend on a named product. Non-trivial design changes get an ADR (currently summarised
inline in D3 Section 14; split into `design/adrs/NNNN-title.md` as they grow).

Changes that add or alter behaviour should reference the relevant D2 requirement ID and, if
architectural, an ADR.

**Traceability is mechanical, not manual.**
[design/traceability/requirements-traceability-matrix.md](design/traceability/requirements-traceability-matrix.md)
is generated from the capability specs by `tools/build-rtm.py` and verified in CI - never edit it
by hand. Adding or changing a requirement in `specs/capabilities/` means regenerating it. Record
delivery evidence in `design/traceability/delivery-map.json`, and name tests after the requirement
they prove (`CAP_SCM_010_bundle_round_trips_without_loss`) so an auditor can go from a requirement
ID to its test without reading code.

The current increment is planned in [design/iteration-1.md](design/iteration-1.md).

## Writing conventions

- Markdown is **ASCII-only** (note the specs use "-" where you'd expect an em dash, and spell out
  "->"). Match the surrounding style rather than introducing Unicode punctuation.
- **Code comments, commit messages, and identifiers are ASCII-only too** - same rule, same reason:
  no em dashes, smart quotes, arrows, or accented characters in source. Non-ASCII belongs only in
  content and localisation *data* (label text, translations, terminology designations), never in
  the code that handles it. CI enforces this across every tracked text file, so a stray em dash in
  a comment fails the build.
- Diagrams are authored as **Mermaid inline** in the specs; `design/diagrams/` holds exports only.
- `.editorconfig`: UTF-8, LF, final newline, 2-space indent (4 for `.cs`/`.py`), and
  trailing-whitespace trimming is **off** for `.md` (Markdown line breaks).

## Domain invariants worth knowing before writing anything

These are non-obvious constraints that cut across the whole design; getting them wrong produces
plausible-looking but incorrect work:

- **Two PDF lineages are distinct and never interchanged.** *Rendered* PDF/HTML are produced by
  the system from FHIR; *artwork* PDF is produced externally by agencies and only ingested and
  linked. Separate object classes in the asset store (D1 Section 3.3, D3 Section 3.2).
- **FHIR is the single source of truth.** The FHIR Content Core holds only canonical structured
  content; every render lives in the asset store keyed to a label version *and* a render-template
  version.
- **Reusable content units are pinned by default** (a label pins the unit version approved with
  it), with opt-in track-latest - a unit change is an explicit propagation via capability #8, never
  a silent update (ADR-007).
- **Per-market regulatory-approval state is modelled separately from internal lifecycle state** - a
  version can be approved in one market and not another (ADR-005).
- **Config-as-data is the extensibility hinge:** a new market, regulator, or rule must be a change
  under `config/`, `policies/`, or `profiles/` - not a service code release (ADR-012, capability 21).
- **No service reaches another service's datastore**; services integrate via APIs and the Kafka
  event backbone, and external systems only through adapters with anti-corruption layers (#24).
- **GxP posture is architectural, not a later add-on:** immutable versions, append-only audit,
  e-signature at approval gates, and deterministic/traceable release are assumed everywhere
  (21 CFR Part 11, EU Annex 11, GAMP 5).
- Test data in this domain must be **synthetic** - no real product or personal data.

## Implementation stack (per D3 Section 12, when code starts landing)

.NET/C# domain services, TypeScript/React UI, Python for data tooling. Adopted OSS components -
HAPI FHIR (R5, the canonical store), Snowstorm terminology, Keycloak, Kafka, MinIO (object-lock
WORM), OpenSearch, OPA, Gotenberg - are **configured under `deploy/`, not built under `src/`**.
The service-to-capability mapping is in [src/README.md](src/README.md); D3 Section 2.1 gives each
service's responsibility. Deployment progression is Docker Compose (dev) -> Kubernetes (test/prod)
-> optional Azure/AKS, and **no component is adopted unless it has a maintained container image**
(ADR-014).

Gotchas already established in the stack: Snowstorm requires Elasticsearch 8.x specifically (the
separate OpenSearch instance serves the content-search index, capability 15); HAPI is pinned to
FHIR **R5**; SNOMED CT and MedDRA content requires licences and is loaded separately.

## Dependencies must be open source and Apache-2.0 compatible

This is a public, **Apache-2.0** project (see [LICENSE](LICENSE) and [NOTICE](NOTICE)), and D3
ADR-001 commits the platform to an open-source, self-hostable stack. Every package, library,
container image, tool, and third-party component you introduce must be open source and carry a
licence compatible with distributing this repository under Apache-2.0. Check the licence *before*
adding a dependency, not in review.

- **Acceptable:** Apache-2.0, MIT, BSD-2-Clause, BSD-3-Clause, ISC, and other permissive licences.
  MPL-2.0 and EPL-2.0 are acceptable for a component consumed unmodified as a separate artifact.
- **Not acceptable:** GPL-2.0/3.0, LGPL where it would reach our distribution, AGPL-3.0, SSPL, and
  source-available/non-compete licences (BSL, Elastic License, Confluent Community). CI blocks
  several of these - see [.github/workflows/dependency-review.yml](.github/workflows/dependency-review.yml).
- **Commercial or proprietary components require an ADR and maintainer approval.** D3 names some as
  alternatives (Ontoserver, Prince/Antenna House, Firely Server); none is adopted by default. Do not
  reach for one because it is easier.
- Prefer a component that is already in the D3 Section 12 stack table over adding a new one. A new
  entry in that table is an architecture decision: record it as an ADR, and confirm it ships a
  maintained container image (ADR-014).
- Update [NOTICE](NOTICE) when a dependency's licence requires attribution.

Two distinctions that matter here and are easy to conflate:

- **Software licence vs content licence.** SNOMED CT, MedDRA, EDQM, and LOINC are *content* under
  their own terms; an open-source terminology server does not make its loaded content free to
  redistribute. Never commit licensed terminology content to this repository.
- **Container dependency vs linked dependency.** The dev stack pins Elasticsearch 8.11.1 because
  Snowstorm requires it, and that image is SSPL/Elastic-licensed - tolerable as an unmodified,
  separately-run container that we do not redistribute, but it is a known exception, not a
  precedent. Do not take a copyleft or source-available licence into our own build or distribution
  on the strength of it.
