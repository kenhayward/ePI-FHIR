#!/usr/bin/env python3
"""Refuse a container image that is not pinned to something that cannot move.

Twice now a pull request has claimed every image was pinned and left one behind: PR 38 pinned
nine `:latest` tags and missed `gotenberg:8`, and pinning that one turned up `postgres:16`,
`keycloak:26.0` and `rabbitmq:3-management` as well. Each was found by reading, which is the
wrong instrument for the job - a moving tag looks exactly like a fixed one.

What counts as pinned:

  - a digest (`@sha256:...`), which cannot move by definition;
  - a version with a patch component (`1.19.0`, `v3.13.2`);
  - a dated release tag (`RELEASE.2025-09-07T16-13-09Z`).

Anything else - a bare major, a major.minor, a variant tag like `3-management`, and `latest`
above all - has to carry a `# pin-ok: <reason>` comment on the line before it. That is not a
loophole: `postgres:16.15` is a complete PostgreSQL version and would otherwise fail, so the
alternative is an allowlist in this file that goes stale silently. A reason next to the image is
read by whoever changes it.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
COMPOSE = ROOT / "deploy" / "docker-compose" / "docker-compose.yml"

IMAGE = re.compile(r"^\s*image:\s*(\S+)\s*$")
JUSTIFIED = re.compile(r"#\s*pin-ok:\s*\S")

# A patch component, optionally prefixed with "v"; or a dated release tag.
PINNED_TAG = re.compile(r"^v?\d+\.\d+\.\d+")
DATED_TAG = re.compile(r"^RELEASE\.")


def pinned(image):
    """Whether this reference names something that cannot change under us."""
    if "@sha256:" in image:
        return True

    # Split off the tag, taking care not to mistake a registry port for one.
    name, separator, tag = image.rpartition(":")
    if not separator or "/" in tag:
        return False

    return bool(PINNED_TAG.match(tag) or DATED_TAG.match(tag))


def main():
    if not COMPOSE.exists():
        sys.exit(f"{COMPOSE}: not found")

    lines = COMPOSE.read_text(encoding="utf-8").splitlines()
    problems = []

    for number, line in enumerate(lines, start=1):
        match = IMAGE.match(line)
        if not match:
            continue

        image = match.group(1)
        if pinned(image):
            continue

        # A justification on any of the three lines above it, which is where a comment
        # explaining an image naturally sits.
        context = lines[max(0, number - 4):number - 1]
        if any(JUSTIFIED.search(previous) for previous in context):
            continue

        problems.append(
            f"{COMPOSE.name}:{number}: '{image}' is not pinned to a digest, a version with a "
            "patch component, or a dated release - and carries no '# pin-ok: <reason>' comment. "
            "A tag that can move makes a deployment unreproducible (ADR-014)."
        )

    for problem in problems:
        print(f"::error file=deploy/docker-compose/docker-compose.yml::{problem}")

    if problems:
        sys.exit(1)

    print("Every container image is pinned to something that cannot move.")


if __name__ == "__main__":
    main()
