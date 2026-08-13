#!/usr/bin/env python3
"""Verify the vendored FHIR conformance packages against their recorded digests.

A pinned package is only a pin if the bytes are the ones that were reviewed. This checks
that every package named in profiles/packages/manifest.json is present, matches its recorded
SHA-256, and declares the version and FHIR version the manifest claims. It also checks that
the dependency closure is complete, so a package cannot quietly require something that is not
vendored and would be fetched over the network at validation time (ADR-016 decision 3).

Usage (from the repository root):
    python tools/verify-profile-packages.py
"""

import hashlib
import json
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGES = ROOT / "profiles" / "packages"
MANIFEST = PACKAGES / "manifest.json"

# Supplied by the validator rather than vendored; see manifest.json "notProvidedHere".
PROVIDED_ELSEWHERE = {"hl7.fhir.r5.core"}


def main():
    if not MANIFEST.exists():
        sys.exit(f"No manifest at {MANIFEST.relative_to(ROOT).as_posix()}")

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    pinned = manifest["packages"]
    problems = []

    for name, entry in pinned.items():
        path = PACKAGES / entry["file"]
        if not path.exists():
            problems.append(f"{name}: {entry['file']} is missing")
            continue

        data = path.read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        if actual != entry["sha256"]:
            problems.append(
                f"{name}: digest mismatch\n"
                f"      recorded {entry['sha256']}\n"
                f"      actual   {actual}")
            continue

        with tarfile.open(path, "r:gz") as archive:
            members = [m for m in archive.getnames() if m.endswith("package/package.json")]
            if not members:
                problems.append(f"{name}: no package/package.json inside {entry['file']}")
                continue
            inner = json.load(archive.extractfile(members[0]))

        if inner.get("name") != name:
            problems.append(f"{name}: package declares name {inner.get('name')!r}")
        if inner.get("version") != entry["version"]:
            problems.append(
                f"{name}: package declares version {inner.get('version')!r}, "
                f"manifest says {entry['version']!r}")
        if inner.get("fhirVersions") != entry["fhirVersions"]:
            problems.append(
                f"{name}: package declares fhirVersions {inner.get('fhirVersions')!r}, "
                f"manifest says {entry['fhirVersions']!r}")

        # Offline resolution needs the whole closure present, or the validator reaches out.
        for dependency in inner.get("dependencies", {}):
            if dependency not in pinned and dependency not in PROVIDED_ELSEWHERE:
                problems.append(
                    f"{name}: depends on {dependency}, which is neither vendored nor "
                    f"recorded as provided elsewhere")

    if problems:
        sys.exit("Vendored profile packages failed verification:\n  - " + "\n  - ".join(problems))

    total = sum(e["bytes"] for e in pinned.values())
    print(f"Verified {len(pinned)} pinned packages ({total / 1_000_000:.1f} MB), "
          f"digests and dependency closure intact.")


if __name__ == "__main__":
    main()
