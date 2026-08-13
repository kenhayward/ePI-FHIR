#!/usr/bin/env python3
"""Generate the V-model traceability documents from the specifications and the code.

The V model has three legs, each with exactly one source of truth:

    Requirements  -> Design functions      design-functions.json ("satisfies")
    Design funcs  -> Unit tests            DISCOVERED from test code (FN id in the test name)
    Requirements  -> Integration tests     integration-tests.json ("verifies"), the implementing
                                           test DISCOVERED from test code (IT id in the name)

Requirement text, IDs, and priority come from specs/capabilities/; delivery phase is
cross-checked between D1 Section 11 and the D2 group summaries. Nothing is duplicated: the
registries record intent, the code records fact, and the Markdown is generated from both.

Usage (from the repository root):
    python tools/build-traceability.py            # regenerate every matrix
    python tools/build-traceability.py --check    # fail if any is out of date (CI)
"""

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPEC_DIR = ROOT / "specs" / "capabilities"
D1 = ROOT / "specs" / "D1-solution-overview.md"
TRACE = ROOT / "design" / "traceability"

DELIVERY_MAP = TRACE / "delivery-map.json"
DESIGN_FUNCTIONS = TRACE / "design-functions.json"
INTEGRATION_TESTS = TRACE / "integration-tests.json"

OUT_RTM = TRACE / "requirements-traceability-matrix.md"
OUT_FN = TRACE / "design-function-to-unit-test-matrix.md"
OUT_IT = TRACE / "requirement-to-integration-test-matrix.md"
OUT_V = TRACE / "v-model-trace.md"

# Directories that hold implementation and test code, and the extensions worth scanning.
CODE_DIRS = ["src", "apps", "tests"]
CODE_SUFFIXES = {".cs", ".ts", ".tsx", ".js", ".py", ".rego", ".fs", ".java"}
SKIP_DIRS = {"bin", "obj", "node_modules", ".git", "dist", "build"}

CAP_HEADER = re.compile(r"^#\s+Capability\s+(\d+)\s+-\s+(.+?)\s+\(([A-Z]{3})\)\s*$")
REQ_ROW = re.compile(r"^\|\s*(CAP-[A-Z]{3}-\d{3})\s*\|\s*(.+?)\s*\|\s*([MSC])\s*\|\s*$")
D1_ROW = re.compile(r"^\|\s*\*\*(P\d)\*\*\s*\|[^|]*\|\s*([^|]+?)\s*\|")
D1_CAP = re.compile(r"(\d+)\s*(\([^)]*\))?")
D2_SUMMARY_ROW = re.compile(r"^\|\s*(\d+)\s*\|\s*[^|]+?\s*\|\s*[A-Z]{3}\s*\|\s*(P\d)[^|]*\|")

# Identifiers as they appear inside test names or attributes, hyphenated or underscored.
TOKEN = re.compile(r"[A-Za-z0-9_]*(?:CAP|FN)[-_][A-Z]{2,5}[-_]\d{3}[A-Za-z0-9_]*|[A-Za-z0-9_]*IT[-_]\d{3}[A-Za-z0-9_]*")
ID_IN_TOKEN = re.compile(r"((?:CAP|FN)[-_][A-Z]{2,5}[-_]\d{3}|IT[-_]\d{3})")

# D3 Section 2.1 component decomposition, keyed by capability number.
COMPONENT = {
    1: "Authoring & Template Service", 2: "Content Core (FHIR)", 3: "Authoring & Template Service",
    4: "Migration Service", 5: "Master Data Service", 6: "Terminology Service",
    7: "Lifecycle & Workflow Service", 8: "Change & Impact Service",
    9: "Localisation & Translation Service", 10: "Regulatory Profiles Service",
    11: "Validation Service", 12: "Compliance Service", 13: "Rendering Service",
    14: "Publishing Service", 15: "Search Service", 16: "Lifecycle & Workflow Service",
    17: "IAM", 18: "Security", 19: "Audit & e-Signature",
    20: "Notification & Event Backbone", 21: "Configuration & Rules Service",
    22: "Retention & Archival Service", 23: "Reporting & Analytics", 24: "Integration & Adapters",
}

GENERATED_HEADER = [
    "GENERATED FILE - do not edit by hand. Regenerate with",
    "`python tools/build-traceability.py`; CI fails if it is out of date.",
]


def collect():
    """Capabilities and requirements parsed from the capability specifications."""
    capabilities, requirements = {}, []
    for path in sorted(SPEC_DIR.glob("*.md")):
        current = None
        for line in path.read_text(encoding="utf-8").splitlines():
            header = CAP_HEADER.match(line)
            if header:
                current = int(header.group(1))
                capabilities[current] = {
                    "name": header.group(2),
                    "abbr": header.group(3),
                    "source": path.relative_to(ROOT).as_posix(),
                }
                continue
            row = REQ_ROW.match(line)
            if row and current is not None:
                requirements.append({
                    "id": row.group(1), "text": row.group(2),
                    "priority": row.group(3), "capability": current,
                })
    return capabilities, requirements


def phases_from_d1():
    """Primary and extension phases per capability, from the D1 Section 11 roadmap.

    A capability listed plainly ("15") is primary to that phase; one listed with a qualifier
    ("15(+consumer read)") extends into it. D1 uses this convention for capabilities 9 and 10.
    """
    primary, extends = {}, {}
    for line in D1.read_text(encoding="utf-8").splitlines():
        row = D1_ROW.match(line)
        if not row:
            continue
        phase, cell = row.group(1), row.group(2)
        for number, qualifier in D1_CAP.findall(cell):
            number = int(number)
            if qualifier:
                extends.setdefault(number, []).append(phase)
            elif number in primary:
                sys.exit(f"Capability {number} has two primary phases in D1 Section 11.")
            else:
                primary[number] = phase
    return primary, extends


def phases_from_d2():
    """Phase per capability as claimed by each D2 group-summary table."""
    claimed = {}
    for path in sorted(SPEC_DIR.glob("*.md")):
        for line in path.read_text(encoding="utf-8").splitlines():
            row = D2_SUMMARY_ROW.match(line)
            if row:
                claimed[int(row.group(1))] = row.group(2)
    return claimed


def registry(path):
    """Load a hand-maintained registry, dropping "_" keys used for notes."""
    if not path.exists():
        return {}
    raw = json.loads(path.read_text(encoding="utf-8"))
    return {k: v for k, v in raw.items() if not k.startswith("_")}


def discover_tests():
    """Map requirement / design-function / integration-test IDs to the tests naming them.

    Evidence is the code itself: a test called CAP_SCM_010_bundle_round_trips_without_loss or
    carrying a FN-CC-006 trait is the proof that requirement or function is verified. Nothing
    here is hand-maintained, so it cannot go stale.
    """
    found = {}
    for directory in CODE_DIRS:
        base = ROOT / directory
        if not base.exists():
            continue
        for path in base.rglob("*"):
            if not path.is_file() or path.suffix not in CODE_SUFFIXES:
                continue
            if SKIP_DIRS & set(p.name for p in path.parents):
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            for token in TOKEN.findall(text):
                match = ID_IN_TOKEN.search(token)
                if not match:
                    continue
                identifier = match.group(1).replace("_", "-")
                where = f"`{token}` ({path.relative_to(ROOT).as_posix()})"
                found.setdefault(identifier, set()).add(where)
    return {k: sorted(v) for k, v in found.items()}


def table(lines, header, rows):
    lines.append("| " + " | ".join(header) + " |")
    lines.append("|" + "|".join(["---"] * len(header)) + "|")
    lines.extend("| " + " | ".join(r) + " |" for r in rows)


def render_rtm(caps, reqs, delivery, primary, extends):
    counts = {}
    for r in reqs:
        counts[r["capability"]] = counts.get(r["capability"], 0) + 1
    covered = sum(1 for r in reqs if r["id"] in delivery)

    lines = ["# Requirements Traceability Matrix", ""] + GENERATED_HEADER + [
        "",
        "Requirement text, IDs, and priority come from the capability specifications; phase from",
        "D1 Section 11 (cross-checked against the D2 group summaries); component from D3 Section",
        "2.1. Delivery evidence is hand-maintained in `delivery-map.json`.",
        "",
        "This is the cross-cutting artefact called for in the Deliverables Definition Section 8.",
        "For the full V model see [v-model-trace.md](v-model-trace.md).",
        "",
        "## Coverage",
        "",
        f"- Requirements specified: **{len(reqs)}** across **{len(caps)}** capabilities",
        f"- Requirements with delivery evidence: **{covered}**",
        f"- Requirements not yet scheduled: **{len(reqs) - covered}**",
        "",
        "## Capabilities",
        "",
    ]
    table(lines,
          ["#", "Capability", "Abbr", "Phase", "Also in", "Component (D3 Section 2.1)", "Requirements", "Specification"],
          [[str(n), c["name"], c["abbr"], primary.get(n, "-"), ", ".join(extends.get(n, [])) or "-",
            COMPONENT.get(n, "-"), str(counts.get(n, 0)),
            f"[{Path(c['source']).name}](../../{c['source']})"] for n, c in sorted(caps.items())])

    lines += ["", "## Matrix", "",
              "Status values: `planned` (scheduled, not built), `partial` (some aspect delivered),",
              "`done` (delivered with evidence), `-` (not yet scheduled).", ""]
    table(lines,
          ["Requirement", "Cap", "Pri", "Phase", "Component", "Iteration", "Status", "Evidence"],
          [[r["id"], str(r["capability"]), r["priority"], primary.get(r["capability"], "-"),
            COMPONENT.get(r["capability"], "-"),
            delivery.get(r["id"], {}).get("iteration", "-"),
            delivery.get(r["id"], {}).get("status", "-"),
            delivery.get(r["id"], {}).get("evidence", "-")] for r in reqs])

    lines += ["", "## Requirement text", "",
              "Full text of every requirement, for readers without the specifications to hand.", ""]
    for number, cap in sorted(caps.items()):
        lines.append(f"### Capability {number} - {cap['name']} ({cap['abbr']})")
        lines.append("")
        for r in [x for x in reqs if x["capability"] == number]:
            lines.append(f"- **{r['id']}** ({r['priority']}) {r['text']}")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def render_design_functions(functions, discovered, req_text):
    verified = [f for f in functions if discovered.get(f)]
    lines = ["# Design Function to Unit Test Matrix", ""] + GENERATED_HEADER + [
        "",
        "The **descent and the base of the V**: each design function decomposes one or more",
        "capability requirements, and is verified by the unit tests that name it. Functions and",
        "the requirements they satisfy are declared in `design-functions.json`; unit tests are",
        "discovered from the code, so a function shows as unverified until a test names it.",
        "",
        "Name unit tests for the function they verify, for example",
        "`FN_CC_006_serialises_without_content_loss`.",
        "",
        "## Coverage",
        "",
        f"- Design functions declared: **{len(functions)}**",
        f"- Verified by at least one unit test: **{len(verified)}**",
        f"- Not yet verified: **{len(functions) - len(verified)}**",
        "",
        "## Matrix",
        "",
    ]
    rows = []
    for fid, fn in sorted(functions.items()):
        tests = discovered.get(fid, [])
        rows.append([fid, fn.get("name", "-"), fn.get("component", "-"),
                     ", ".join(fn.get("satisfies", [])) or "-",
                     fn.get("iteration", "-"),
                     "verified" if tests else fn.get("status", "-"),
                     "<br>".join(tests) if tests else "-"])
    table(lines, ["Function", "Name", "Component", "Satisfies", "Iteration", "Status", "Unit tests"], rows)

    lines += ["", "## Requirements covered by these functions", ""]
    by_req = {}
    for fid, fn in functions.items():
        for req in fn.get("satisfies", []):
            by_req.setdefault(req, []).append(fid)
    table(lines, ["Requirement", "Design functions", "Requirement text"],
          [[req, ", ".join(sorted(fns)), req_text.get(req, "-")] for req, fns in sorted(by_req.items())])
    return "\n".join(lines).rstrip() + "\n"


def render_integration(tests, discovered, req_text, reqs, delivery):
    implemented = [t for t in tests if discovered.get(t)]
    verified_reqs = {r for t in tests for r in tests[t].get("verifies", [])}
    scheduled = {r["id"] for r in reqs if r["id"] in delivery}
    lines = ["# Requirement to Integration Test Matrix", ""] + GENERATED_HEADER + [
        "",
        "The **ascent of the V**: integration tests validate capability requirements end to end,",
        "against real collaborators rather than mocks. Intent is declared in",
        "`integration-tests.json`; the implementing test is discovered from the code by its IT id.",
        "",
        "Name integration tests for the case they validate, for example",
        "`IT_001_bundle_round_trips_through_create_and_read`.",
        "",
        "## Coverage",
        "",
        f"- Integration tests declared: **{len(tests)}**",
        f"- Implemented in code: **{len(implemented)}**",
        f"- Requirements validated by at least one integration test: **{len(verified_reqs)}**",
        f"- Scheduled requirements still without one: **{len(scheduled - verified_reqs)}**",
        "",
        "## Integration tests",
        "",
    ]
    table(lines, ["Test", "Scenario", "Verifies", "Iteration", "Status", "Implementation"],
          [[tid, t.get("name", "-"), ", ".join(t.get("verifies", [])) or "-", t.get("iteration", "-"),
            "implemented" if discovered.get(tid) else t.get("status", "-"),
            "<br>".join(discovered.get(tid, [])) or "-"] for tid, t in sorted(tests.items())])

    lines += ["", "## By requirement", ""]
    by_req = {}
    for tid, t in tests.items():
        for req in t.get("verifies", []):
            by_req.setdefault(req, []).append(tid)
    table(lines, ["Requirement", "Integration tests", "Requirement text"],
          [[req, ", ".join(sorted(tids)), req_text.get(req, "-")] for req, tids in sorted(by_req.items())])
    return "\n".join(lines).rstrip() + "\n"


def render_vmodel(reqs, delivery, functions, tests, discovered, primary):
    fns_by_req, its_by_req = {}, {}
    for fid, fn in functions.items():
        for req in fn.get("satisfies", []):
            fns_by_req.setdefault(req, []).append(fid)
    for tid, t in tests.items():
        for req in t.get("verifies", []):
            its_by_req.setdefault(req, []).append(tid)

    scheduled = [r for r in reqs if r["id"] in delivery]
    no_fn = [r["id"] for r in scheduled if r["id"] not in fns_by_req]
    no_it = [r["id"] for r in scheduled if r["id"] not in its_by_req]
    fn_no_unit = sorted(f for f in functions if not discovered.get(f))
    it_no_impl = sorted(t for t in tests if not discovered.get(t))

    lines = ["# V-Model Trace", ""] + GENERATED_HEADER + [
        "",
        "The whole chain in one place, for a scheduled requirement:",
        "",
        "```",
        "  Requirement  --------------------------------->  Integration test",
        "  (specs/capabilities)                             (validates the requirement)",
        "        |                                                    ^",
        "        v                                                    |",
        "  Design function  ----------------------------->  Unit test",
        "  (design-functions.json)                          (verifies the function)",
        "```",
        "",
        "Only requirements scheduled in `delivery-map.json` appear below; the full requirement",
        "set is in [requirements-traceability-matrix.md](requirements-traceability-matrix.md).",
        "",
        "## Coverage of scheduled requirements",
        "",
        f"- Scheduled requirements: **{len(scheduled)}**",
        f"- With at least one design function: **{len(scheduled) - len(no_fn)}**",
        f"- With at least one integration test: **{len(scheduled) - len(no_it)}**",
        f"- Design functions awaiting a unit test: **{len(fn_no_unit)}** of {len(functions)}",
        f"- Integration tests awaiting implementation: **{len(it_no_impl)}** of {len(tests)}",
        "",
        "## Trace",
        "",
    ]
    rows = []
    for r in scheduled:
        fns = sorted(fns_by_req.get(r["id"], []))
        its = sorted(its_by_req.get(r["id"], []))
        units = sorted({u for f in fns for u in discovered.get(f, [])})
        impls = sorted({i for t in its for i in discovered.get(t, [])})
        rows.append([r["id"], primary.get(r["capability"], "-"),
                     delivery.get(r["id"], {}).get("iteration", "-"),
                     ", ".join(fns) or "**none**",
                     "<br>".join(units) if units else "-",
                     ", ".join(its) or "**none**",
                     "<br>".join(impls) if impls else "-"])
    table(lines, ["Requirement", "Phase", "Iteration", "Design functions", "Unit tests",
                  "Integration tests", "Implementations"], rows)

    lines += ["", "## Gaps", "",
              "Gaps are reported, not enforced: a requirement scheduled but not yet decomposed is",
              "normal work in progress. Referential errors, by contrast, fail the build.", ""]
    for title, items in [
        ("Scheduled requirements with no design function", no_fn),
        ("Scheduled requirements with no integration test", no_it),
        ("Design functions with no unit test", fn_no_unit),
        ("Integration tests not yet implemented", it_no_impl),
    ]:
        lines.append(f"**{title}:** " + (", ".join(items) if items else "none"))
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def build():
    capabilities, requirements = collect()
    if not requirements:
        sys.exit("No requirements found - check specs/capabilities/ and the row pattern.")
    req_ids = {r["id"] for r in requirements}
    req_text = {r["id"]: r["text"] for r in requirements}

    # D1 Section 11 and the D2 group summaries both state delivery phase. They must agree, and
    # every specified capability must appear in the roadmap. Drift here is a documentation defect
    # that would otherwise surface only when someone plans the iteration that needs it.
    primary, extends = phases_from_d1()
    claimed = phases_from_d2()
    problems = [
        f"  capability {n}: D2 says {claimed[n]}, D1 Section 11 says {primary.get(n) or 'nothing'}"
        for n in sorted(claimed) if claimed[n] != primary.get(n)
    ] + [
        f"  capability {n}: specified in D2 but absent from the D1 Section 11 roadmap"
        for n in sorted(set(capabilities) - set(primary))
    ]
    if problems:
        sys.exit("Delivery phase disagrees between D1 and D2:\n" + "\n".join(problems))

    delivery = registry(DELIVERY_MAP)
    functions = registry(DESIGN_FUNCTIONS)
    tests = registry(INTEGRATION_TESTS)
    discovered = discover_tests()

    # Every reference must resolve, in both directions.
    errors = [f"  delivery-map.json: unknown requirement {r}" for r in sorted(set(delivery) - req_ids)]
    for fid, fn in sorted(functions.items()):
        errors += [f"  design-functions.json: {fid} satisfies unknown requirement {r}"
                   for r in fn.get("satisfies", []) if r not in req_ids]
        if not fn.get("satisfies"):
            errors.append(f"  design-functions.json: {fid} satisfies no requirement")
    for tid, t in sorted(tests.items()):
        errors += [f"  integration-tests.json: {tid} verifies unknown requirement {r}"
                   for r in t.get("verifies", []) if r not in req_ids]
        if not t.get("verifies"):
            errors.append(f"  integration-tests.json: {tid} verifies no requirement")
    known = req_ids | set(functions) | set(tests)
    errors += [f"  test code references unknown identifier {i}: {', '.join(discovered[i])}"
               for i in sorted(set(discovered) - known)]
    if errors:
        sys.exit("Traceability references do not resolve:\n" + "\n".join(errors))

    return {
        OUT_RTM: render_rtm(capabilities, requirements, delivery, primary, extends),
        OUT_FN: render_design_functions(functions, discovered, req_text),
        OUT_IT: render_integration(tests, discovered, req_text, requirements, delivery),
        OUT_V: render_vmodel(requirements, delivery, functions, tests, discovered, primary),
    }, len(requirements), len(functions), len(tests)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="fail if any matrix is stale")
    args = parser.parse_args()

    documents, n_req, n_fn, n_it = build()
    if args.check:
        stale = [p.relative_to(ROOT).as_posix() for p, content in documents.items()
                 if (p.read_text(encoding="utf-8") if p.exists() else "") != content]
        if stale:
            sys.exit("Out of date - run python tools/build-traceability.py:\n  " + "\n  ".join(stale))
        print(f"Traceability is current ({n_req} requirements, {n_fn} design functions, {n_it} integration tests).")
        return

    for path, content in documents.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
    print(f"Wrote {len(documents)} documents to {TRACE.relative_to(ROOT).as_posix()}/ "
          f"({n_req} requirements, {n_fn} design functions, {n_it} integration tests).")


if __name__ == "__main__":
    main()
