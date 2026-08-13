# V-Model Trace

GENERATED FILE - do not edit by hand. Regenerate with
`python tools/build-traceability.py`; CI fails if it is out of date.

The whole chain in one place, for a scheduled requirement:

```
  Requirement  --------------------------------->  Integration test
  (specs/capabilities)                             (validates the requirement)
        |                                                    ^
        v                                                    |
  Design function  ----------------------------->  Unit test
  (design-functions.json)                          (verifies the function)
```

Only requirements scheduled in `delivery-map.json` appear below; the full requirement
set is in [requirements-traceability-matrix.md](requirements-traceability-matrix.md).

## Coverage of scheduled requirements

- Scheduled requirements: **15**
- With at least one design function: **15**
- With at least one integration test: **15**
- Design functions awaiting a unit test: **23** of 23
- Integration tests awaiting implementation: **9** of 9

## Trace

| Requirement | Phase | Iteration | Design functions | Unit tests | Integration tests | Implementations |
|---|---|---|---|---|---|---|
| CAP-SCM-001 | P0 | 1 | FN-CC-001, FN-CC-004, FN-CC-005 | - | IT-001 | - |
| CAP-SCM-007 | P0 | 1 | FN-CC-002, FN-CC-003, FN-CC-007 | - | IT-006 | - |
| CAP-SCM-010 | P0 | 1 | FN-CC-006 | - | IT-001 | - |
| CAP-VAL-003 | P2 | 1 | FN-VAL-001, FN-VAL-002 | - | IT-005 | - |
| CAP-VAL-005 | P2 | 1 | FN-VAL-003 | - | IT-005 | - |
| CAP-IAM-001 | P0 | 1 | FN-IAM-001 | - | IT-007 | - |
| CAP-IAM-002 | P0 | 1 | FN-IAM-002, FN-IAM-003 | - | IT-002 | - |
| CAP-IAM-007 | P0 | 1 | FN-IAM-004 | - | IT-002 | - |
| CAP-IAM-009 | P0 | 1 | FN-AUD-004 | - | IT-003 | - |
| CAP-AUD-001 | P0 | 1 | FN-AUD-001 | - | IT-003 | - |
| CAP-AUD-002 | P0 | 1 | FN-AUD-002, FN-AUD-003 | - | IT-003 | - |
| CAP-EVT-001 | P2 | 1 | FN-EVT-001, FN-EVT-002 | - | IT-008 | - |
| CAP-CFG-001 | P0 | 1 | FN-CFG-001, FN-CFG-002 | - | IT-004 | - |
| CAP-CFG-004 | P0 | 1 | FN-CFG-001 | - | IT-004 | - |
| CAP-CFG-006 | P0 | 1 | FN-CFG-003 | - | IT-009 | - |

## Gaps

Gaps are reported, not enforced: a requirement scheduled but not yet decomposed is
normal work in progress. Referential errors, by contrast, fail the build.

**Scheduled requirements with no design function:** none

**Scheduled requirements with no integration test:** none

**Design functions with no unit test:** FN-AUD-001, FN-AUD-002, FN-AUD-003, FN-AUD-004, FN-CC-001, FN-CC-002, FN-CC-003, FN-CC-004, FN-CC-005, FN-CC-006, FN-CC-007, FN-CFG-001, FN-CFG-002, FN-CFG-003, FN-EVT-001, FN-EVT-002, FN-IAM-001, FN-IAM-002, FN-IAM-003, FN-IAM-004, FN-VAL-001, FN-VAL-002, FN-VAL-003

**Integration tests not yet implemented:** IT-001, IT-002, IT-003, IT-004, IT-005, IT-006, IT-007, IT-008, IT-009
