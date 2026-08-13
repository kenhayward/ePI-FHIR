# design/traceability/ - Requirements traceability

The cross-cutting artefact called for in the Deliverables Definition Section 8, and the spine of
the GxP/CSV traceability chain in D3 Section 8.4: **requirement -> design -> delivery -> test**.

## Contents

- `requirements-traceability-matrix.md` - **generated, do not edit by hand.** Every requirement
  in `specs/capabilities/`, with its capability, priority, delivery phase (D1 Section 11), owning
  component (D3 Section 2.1), and delivery evidence.
- `delivery-map.json` - the one hand-maintained input: which iteration commits to a requirement,
  its status, and the evidence that proves it.

## Regenerating

```bash
python tools/build-rtm.py            # regenerate
python tools/build-rtm.py --check    # fail if stale (this is what CI runs)
```

The matrix is regenerated from the specifications on every change, so it cannot drift: adding a
requirement to a capability specification without regenerating fails CI.

## Recording delivery

When a requirement is implemented, add or update its entry in `delivery-map.json` and regenerate:

```json
"CAP-SCM-010": {"iteration": "1", "status": "done", "evidence": "Epi.ContentCore.Tests CAP_SCM_010_bundle_round_trips_without_loss"}
```

Status values: `planned` (scheduled, not built), `partial` (some aspect delivered), `done`
(delivered with evidence). Evidence should name the test that proves the requirement, so an
auditor can go from a requirement ID to the test that demonstrates it without reading the code.
Test names carry their requirement ID for exactly this reason (see `design/iteration-1.md`
Section 7).

## Scope note

The matrix covers requirement-to-delivery. It does not replace the ADRs
(`design/adrs/`), which record *why* a design decision was made; the matrix records *where* a
requirement is satisfied.
