# design/ - Technical design (the "how")

How the platform is built. Prescriptive architecture and the decisions behind it.

## Contents
- `D3-technical-architecture.md` - logical/data/API/integration architecture, security, deployment,
  NFRs, the technology stack (open-source primary; Azure future target), runtime scenarios, and ADRs.
- `iteration-1.md` - the first buildable increment: scope, acceptance criteria, decisions it
  forces, and delivery sequence.
- `adrs/` - Architecture Decision Records (currently summarised inline in D3 Section 14; split into
  individual records here as they evolve).
- `traceability/` - V-model traceability: requirement -> design function -> unit test, and
  requirement -> integration test. Registries are hand-maintained, the matrices are generated.
- `diagrams/` - exported diagrams (authored as Mermaid inline in the specs; export via `tools/`).

Traceability: capability requirements in `specs/` (D2 IDs) map to services/components in D3, and
to the tests that prove them, via `traceability/requirements-traceability-matrix.md`.
