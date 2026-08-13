#!/usr/bin/env python3
"""Generate the requirements traceability matrix from the capability specifications.

The matrix is derived, never hand-edited: requirement IDs, text, and priority come
from specs/capabilities/*.md, the phase from D1 Section 11, and the owning component
from D3 Section 2.1. Delivery evidence (which iteration implements a requirement and
what proves it) is the one hand-maintained input, held in delivery-map.json.

Usage (from the repository root):
    python tools/build-rtm.py            # regenerate the matrix
    python tools/build-rtm.py --check    # fail if the matrix is out of date (CI)
"""

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPEC_DIR = ROOT / "specs" / "capabilities"
D1 = ROOT / "specs" / "D1-solution-overview.md"
OUT = ROOT / "design" / "traceability" / "requirements-traceability-matrix.md"
DELIVERY_MAP = ROOT / "design" / "traceability" / "delivery-map.json"

# D3 Section 2.1 component decomposition.
COMPONENT = {
    1: "Authoring & Template Service",
    2: "Content Core (FHIR)",
    3: "Authoring & Template Service",
    4: "Migration Service",
    5: "Master Data Service",
    6: "Terminology Service",
    7: "Lifecycle & Workflow Service",
    8: "Change & Impact Service",
    9: "Localisation & Translation Service",
    10: "Regulatory Profiles Service",
    11: "Validation Service",
    12: "Compliance Service",
    13: "Rendering Service",
    14: "Publishing Service",
    15: "Search Service",
    16: "Lifecycle & Workflow Service",
    17: "IAM",
    18: "Security",
    19: "Audit & e-Signature",
    20: "Notification & Event Backbone",
    21: "Configuration & Rules Service",
    22: "Retention & Archival Service",
    23: "Reporting & Analytics",
    24: "Integration & Adapters",
}

CAP_HEADER = re.compile(r"^#\s+Capability\s+(\d+)\s+-\s+(.+?)\s+\(([A-Z]{3})\)\s*$")
REQ_ROW = re.compile(r"^\|\s*(CAP-[A-Z]{3}-\d{3})\s*\|\s*(.+?)\s*\|\s*([MSC])\s*\|\s*$")
# D1 Section 11 roadmap row, e.g. "| **P1** | Author -> manage | 1, 3, 7, 9, 15, 16 | ... |"
D1_ROW = re.compile(r"^\|\s*\*\*(P\d)\*\*\s*\|[^|]*\|\s*([^|]+?)\s*\|")
# A capability in a roadmap cell, optionally qualified: "15" or "15(+consumer read)"
D1_CAP = re.compile(r"(\d+)\s*(\([^)]*\))?")
# D2 group-summary row, e.g. "| 15 | Search, Access & Retrieval | SCH | P1 | ... |"
D2_SUMMARY_ROW = re.compile(r"^\|\s*(\d+)\s*\|\s*[^|]+?\s*\|\s*[A-Z]{3}\s*\|\s*(P\d)[^|]*\|")


def phases_from_d1():
    """Primary and extension phases per capability, parsed from the D1 Section 11 roadmap.

    A capability listed plainly ("15") is primary to that phase; one listed with a
    qualifier ("15(+consumer read)") extends into it. D1 already uses this convention
    for capabilities 9 and 10.
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
    """Phase per capability, as claimed by each D2 group-summary table."""
    claimed = {}
    for path in sorted(SPEC_DIR.glob("*.md")):
        for line in path.read_text(encoding="utf-8").splitlines():
            row = D2_SUMMARY_ROW.match(line)
            if row:
                claimed[int(row.group(1))] = row.group(2)
    return claimed


def collect():
    """Return (capabilities, requirements) parsed from the capability specs."""
    capabilities, requirements = {}, []
    for path in sorted(SPEC_DIR.glob("*.md")):
        current = None
        for line in path.read_text(encoding="utf-8").splitlines():
            header = CAP_HEADER.match(line)
            if header:
                number, name, abbr = int(header.group(1)), header.group(2), header.group(3)
                current = number
                capabilities[number] = {
                    "name": name,
                    "abbr": abbr,
                    "source": path.relative_to(ROOT).as_posix(),
                }
                continue
            row = REQ_ROW.match(line)
            if row and current is not None:
                requirements.append(
                    {
                        "id": row.group(1),
                        "text": row.group(2),
                        "priority": row.group(3),
                        "capability": current,
                    }
                )
    return capabilities, requirements


def render(capabilities, requirements, delivery, primary, extends):
    caps_by_number = dict(sorted(capabilities.items()))
    counts = {}
    for req in requirements:
        counts[req["capability"]] = counts.get(req["capability"], 0) + 1

    covered = sum(1 for r in requirements if r["id"] in delivery)
    lines = [
        "# Requirements Traceability Matrix",
        "",
        "GENERATED FILE - do not edit by hand. Regenerate with `python tools/build-rtm.py`;",
        "CI fails if it is out of date. Requirement text, IDs, and priority come from the",
        "capability specifications; phase from D1 Section 11; component from D3 Section 2.1.",
        "Delivery evidence is the only hand-maintained input, in `delivery-map.json`.",
        "",
        "This is the cross-cutting artefact called for in the Deliverables Definition",
        "Section 8, and the spine of the GxP/CSV traceability chain (D3 Section 8.4):",
        "requirement -> design -> delivery -> test.",
        "",
        "## Coverage",
        "",
        f"- Requirements specified: **{len(requirements)}** across **{len(caps_by_number)}** capabilities",
        f"- Requirements with delivery evidence: **{covered}**",
        f"- Requirements not yet scheduled: **{len(requirements) - covered}**",
        "",
        "## Capabilities",
        "",
        "| # | Capability | Abbr | Phase | Also in | Component (D3 Section 2.1) | Requirements | Specification |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for number, cap in caps_by_number.items():
        lines.append(
            "| {} | {} | {} | {} | {} | {} | {} | [{}]({}) |".format(
                number,
                cap["name"],
                cap["abbr"],
                primary.get(number, "-"),
                ", ".join(extends.get(number, [])) or "-",
                COMPONENT.get(number, "-"),
                counts.get(number, 0),
                Path(cap["source"]).name,
                "../../" + cap["source"],
            )
        )

    lines += [
        "",
        "## Matrix",
        "",
        "Status values: `planned` (scheduled, not built), `partial` (some aspect delivered),",
        "`done` (delivered with evidence), blank (not yet scheduled).",
        "",
        "| Requirement | Cap | Pri | Phase | Component | Iteration | Status | Evidence |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for req in requirements:
        number = req["capability"]
        entry = delivery.get(req["id"], {})
        lines.append(
            "| {} | {} | {} | {} | {} | {} | {} | {} |".format(
                req["id"],
                number,
                req["priority"],
                primary.get(number, "-"),
                COMPONENT.get(number, "-"),
                entry.get("iteration", "-"),
                entry.get("status", "-"),
                entry.get("evidence", "-"),
            )
        )

    lines += [
        "",
        "## Requirement text",
        "",
        "Full text of every requirement, for readers without the specifications to hand.",
        "",
    ]
    for number, cap in caps_by_number.items():
        lines.append(f"### Capability {number} - {cap['name']} ({cap['abbr']})")
        lines.append("")
        for req in [r for r in requirements if r["capability"] == number]:
            lines.append(f"- **{req['id']}** ({req['priority']}) {req['text']}")
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="fail if the matrix is stale")
    args = parser.parse_args()

    capabilities, requirements = collect()
    if not requirements:
        sys.exit("No requirements found - check specs/capabilities/ and the row pattern.")

    # D1 Section 11 and the D2 group summaries both state delivery phase. They must agree,
    # and every specified capability must appear in the roadmap. Drift here is a documentation
    # defect that would otherwise surface only when someone plans an iteration.
    primary, extends = phases_from_d1()
    claimed = phases_from_d2()
    problems = [
        f"  capability {n}: D2 says {claimed[n]}, D1 Section 11 says {primary.get(n) or 'nothing'}"
        for n in sorted(claimed)
        if claimed[n] != primary.get(n)
    ]
    problems += [
        f"  capability {n}: specified in D2 but absent from the D1 Section 11 roadmap"
        for n in sorted(set(capabilities) - set(primary))
    ]
    if problems:
        sys.exit("Delivery phase disagrees between D1 and D2:\n" + "\n".join(problems))

    raw = json.loads(DELIVERY_MAP.read_text(encoding="utf-8")) if DELIVERY_MAP.exists() else {}
    delivery = {k: v for k, v in raw.items() if not k.startswith("_")}  # "_" keys are notes
    unknown = sorted(set(delivery) - {r["id"] for r in requirements})
    if unknown:
        sys.exit("delivery-map.json references unknown requirement IDs: " + ", ".join(unknown))

    content = render(capabilities, requirements, delivery, primary, extends)
    if args.check:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current != content:
            sys.exit(f"{OUT.relative_to(ROOT).as_posix()} is out of date - run python tools/build-rtm.py")
        print(f"Traceability matrix is current ({len(requirements)} requirements).")
        return

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(content, encoding="utf-8", newline="\n")
    print(f"Wrote {OUT.relative_to(ROOT).as_posix()} ({len(requirements)} requirements).")


if __name__ == "__main__":
    main()
