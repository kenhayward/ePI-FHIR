# design/traceability/ - V-model traceability

The cross-cutting artefact called for in the Deliverables Definition Section 8, and the spine of
the GxP/CSV traceability chain in D3 Section 8.4. It maps a traditional V model:

```
  Requirement  ------------------------------------->  Integration test
  (specs/capabilities)                                  validates the requirement
        |                                                        ^
        v                                                        |
  Design function  --------------------------------->  Unit test
  (design-functions.json)                               verifies the function
```

Each leg has exactly **one** source of truth, and nothing is written down twice:

| Leg | Source of truth | Maintained how |
|---|---|---|
| Requirement -> design function | `design-functions.json` (`satisfies`) | By hand, when a function is designed |
| Design function -> unit test | the test code | **Discovered** from the FN id in the test name |
| Requirement -> integration test | `integration-tests.json` (`verifies`) | By hand, when a scenario is specified |
| Integration test -> implementation | the test code | **Discovered** from the IT id in the test name |
| Requirement -> iteration and status | `delivery-map.json` | By hand, as work is scheduled |

The two "discovered" legs are why this cannot rot. Nobody has to remember to update a matrix
after writing a test: the test's own name is the evidence, so the matrix is a view of reality
rather than a claim about it.

## Naming convention

The convention is load-bearing - it is the entire discovery mechanism.

| Test kind | Name it for | Example |
|---|---|---|
| Unit test | the design function it verifies | `FN_CC_006_serialises_without_content_loss` |
| Integration test | the scenario it validates | `IT_001_bundle_round_trips_through_create_and_read` |
| Either | the requirement, where that reads better | `CAP_SCM_010_round_trip_is_lossless` |

Hyphenated forms are recognised too, so a trait or attribute such as
`[Trait("requirement", "CAP-SCM-010")]` counts as evidence.

## Generated documents

Do not edit these by hand; they are regenerated and diffed in CI.

- `requirements-traceability-matrix.md` - every requirement, with phase, component, and delivery
  status. The index.
- `design-function-to-unit-test-matrix.md` - the descent and base of the V.
- `requirement-to-integration-test-matrix.md` - the ascent of the V.
- `v-model-trace.md` - the whole chain per scheduled requirement, plus gap analysis.

## Regenerating

```bash
python tools/build-traceability.py            # regenerate all four
python tools/build-traceability.py --check    # fail if any is stale (this is what CI runs)
```

## What fails the build, and what does not

**Fails** (these are defects):

- A design function or integration test referencing a requirement ID that does not exist.
- A design function satisfying no requirement, or an integration test verifying nothing.
- A test naming a design function or integration test that is not in the registry - typically a
  typo, or a registry entry deleted while its test remains.
- Delivery phase disagreeing between the D1 Section 11 roadmap and a D2 group summary, or a
  specified capability missing from the roadmap entirely. This check exists because exactly that
  drift occurred: capability 15 was phased P1 in D2.5 but omitted from D1, and nothing would have
  caught it until someone planned the iteration that needed it.
- Any generated document being out of date.

**Reported but allowed** (these are work in progress):

- A scheduled requirement with no design function or no integration test yet.
- A design function with no unit test, or an integration test not yet implemented.

Gap counts and lists are in `v-model-trace.md`. Under test-driven development these gaps close
before the implementation exists, not after.

## Adding to the registries

A new design function:

```json
"FN-CC-008": {"name": "Resolve a reusable content unit to its pinned version", "component": "Content Core (FHIR)", "satisfies": ["CAP-SCM-004"], "iteration": "2", "status": "planned"}
```

A new integration test:

```json
"IT-010": {"name": "A pinned reusable unit does not change when a newer version is published", "verifies": ["CAP-SCM-004"], "iteration": "2", "status": "planned"}
```

Then regenerate. Status in the registries is the *intent*; where the code proves otherwise the
generated matrix shows `verified` or `implemented` instead, because the code wins.

## Scope note

These matrices record *where* a requirement is satisfied and *what* proves it. They do not
replace the ADRs in `design/adrs/`, which record *why* a decision was made.
