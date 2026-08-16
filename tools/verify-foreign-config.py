#!/usr/bin/env python3
"""Refuse an explanatory key in configuration a third party parses.

This repository's own configuration carries its reasoning inline, in `_comment` keys, and that
is deliberate: the person changing a market or a routing rule should find the argument next to
the thing they are changing. Our loaders map those keys explicitly, so they are part of the
format rather than tolerated by it.

Configuration a third party parses is a different matter, and assuming otherwise cost a
deployment. A `_comment` added to a client in the Keycloak realm seed made the realm import fail
outright - `ClientRepresentation` rejects unknown fields - which stopped Keycloak, which stopped
the API and HAPI FHIR that wait on it. Three services down, and the error named a Jackson field
mapping rather than anything a reader would connect to a comment.

Nothing caught it. The JSON was valid JSON, `docker compose config -q` does not import realms,
and no test starts Keycloak. It surfaced only when the stack was next brought up by hand.

So: files owned by somebody else's parser carry no keys of ours. The reasoning for those lives
in deploy/docker-compose/README.md, where a reader will find it and no parser will.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Configuration consumed by a third party's schema. Each is a file we write and something else
# reads, which is exactly the combination where an unknown key is somebody else's error message.
FOREIGN = [
    Path("deploy/docker-compose/init/keycloak-epi-realm.json"),
]


def offending_keys(node, path=""):
    """Every key of ours in something we do not own the schema of."""
    if isinstance(node, dict):
        for key, value in node.items():
            if key.startswith("_"):
                yield f"{path}.{key}".lstrip(".")
            yield from offending_keys(value, f"{path}.{key}")
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from offending_keys(value, f"{path}[{index}]")


def main():
    problems = []

    for relative in FOREIGN:
        path = ROOT / relative
        if not path.exists():
            problems.append(f"{relative.as_posix()}: not found.")
            continue

        try:
            parsed = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as invalid:
            problems.append(f"{relative.as_posix()}: not valid JSON - {invalid}")
            continue

        for key in offending_keys(parsed):
            problems.append(
                f"{relative.as_posix()}: '{key}' is an explanatory key in configuration a third "
                "party parses, and it will be rejected as an unknown field rather than ignored. "
                "Put the reasoning in deploy/docker-compose/README.md instead."
            )

    for problem in problems:
        print(f"::error file=deploy/docker-compose/init/keycloak-epi-realm.json::{problem}")

    if problems:
        sys.exit(1)

    print(f"No explanatory keys in third-party configuration ({len(FOREIGN)} file(s) checked).")


if __name__ == "__main__":
    main()
