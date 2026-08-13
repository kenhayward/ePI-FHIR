# specs/ - Specifications (the "what")

The functional source of truth for the platform. Written in Markdown (ASCII-only), these define
scope and behaviour independent of implementation.

## Contents
- `deliverables-definition.md` - the deliverable set, boundaries, and the 24-capability catalogue.
- `D1-solution-overview.md` - problem, scope, domain primer, stakeholders, capability map,
  guiding architecture, principles, NFR summary, roadmap.
- `capabilities/` - the Detailed Capability Specifications (D2), one document per domain group:
  - `D2.1-content-and-authoring.md` (capabilities 1-4)
  - `D2.2-reference-and-master-data.md` (5-6)
  - `D2.3-lifecycle-change-localisation.md` (7-10)
  - `D2.4-quality-and-production.md` (11-14)
  - `D2.5-access-and-governance.md` (15-19)
  - `D2.6-platform-and-operations.md` (20-24)

## Conventions
- Requirement IDs: `CAP-<abbr>-NNN`. Priority: M/S/C.
- Each capability follows a fixed template (purpose, actors/flows, requirements, data model,
  standards mapping, owned state, business rules, interfaces, acceptance criteria, dependencies).
- Implementation and product choices live in `design/`, not here.
