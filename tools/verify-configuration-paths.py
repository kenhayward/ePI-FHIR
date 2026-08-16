#!/usr/bin/env python3
"""Refuse a configuration path the container declares but nothing mounts, or reads but never declares.

Three defects now share one shape, and each was found by running the walkthrough rather than by
CI:

  - `Epi__Workflow__RoutingPath` was never set in the container, so routing loaded as absent and
    no task was ever raised. Nothing failed; nobody was asked to review anything.
  - `Epi__Lifecycle__MarketStatesPath` and the market state model, same story, earlier.
  - The routing path pointed at `label-routing.json` after that file became a directory.

They are invisible to a test suite because a test host sets its own paths, and invisible to a
compose smoke test because a service that has silently loaded no configuration still starts,
still answers `/health`, and still looks entirely well. The failure only appears later, as
something not happening.

So this checks the three surfaces against each other, statically:

  1. Every path-typed setting `Program.cs` reads is declared in the Dockerfile. A setting the
     application looks for and the image never provides is the first defect exactly.
  2. Every path the Dockerfile declares resolves to something that exists, given what the compose
     stack mounts at that location. That is the third defect exactly.
  3. Every path the Dockerfile declares is under a location something actually mounts. A path
     nobody mounts is a path that will be empty at runtime.

What this cannot check is a service reading a setting it never declares a default for, which is
why the application also resolves every one of these at start-up and refuses to run without them
(src/Epi.Api/Program.cs). This is the half CI can see.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROGRAM = ROOT / "src" / "Epi.Api" / "Program.cs"
DOCKERFILE = ROOT / "src" / "Epi.Api" / "Dockerfile"
COMPOSE = ROOT / "deploy" / "docker-compose" / "docker-compose.yml"

# A setting whose value is a path. Named by convention - the key ends in "Path" - which is a
# convention worth keeping precisely because this check depends on it.
READ_SETTING = re.compile(r'builder\.Configuration\["(Epi:[A-Za-z:]*Path)"\]')

# ENV lines in the Dockerfile, which use the double-underscore form.
DECLARED = re.compile(r"^\s*(?:ENV\s+)?(Epi__[A-Za-z_]*Path)=(\S+)")

# A read-only bind of a repository directory into the container.
MOUNT = re.compile(r"^\s*-\s*(\.\./\.\./|\./)?(\S+?):(/\S+?)(?::ro)?\s*$")


def settings_read():
    """The path-typed settings the application reads, in the colon form."""
    return {match.group(1) for match in READ_SETTING.finditer(PROGRAM.read_text(encoding="utf-8"))}


def settings_declared():
    """The path-typed settings the image declares, mapped to the paths they name."""
    declared = {}
    for line in DOCKERFILE.read_text(encoding="utf-8").splitlines():
        match = DECLARED.match(line.strip().lstrip("\\").strip())
        if match:
            declared[match.group(1)] = match.group(2).rstrip("\\").strip()
    return declared


def mounts():
    """Container paths the compose stack fills, mapped to the repository directory behind each."""
    found = {}
    for line in COMPOSE.read_text(encoding="utf-8").splitlines():
        match = MOUNT.match(line)
        if not match:
            continue

        prefix, source, target = match.groups()
        if prefix == "../../":
            found[target] = ROOT / source
        elif prefix == "./":
            found[target] = COMPOSE.parent / source

    return found


def resolve(container_path, filled):
    """The repository path behind a container path, or None if nothing mounts it."""
    for target, source in filled.items():
        if container_path == target:
            return source
        if container_path.startswith(target.rstrip("/") + "/"):
            return source / container_path[len(target.rstrip("/")) + 1:]

    return None


def main():
    for required in (PROGRAM, DOCKERFILE, COMPOSE):
        if not required.exists():
            sys.exit(f"{required}: not found")

    read = settings_read()
    declared = settings_declared()
    filled = mounts()
    problems = []

    for setting in sorted(read):
        env = setting.replace(":", "__")
        if env not in declared:
            problems.append(
                f"Dockerfile: the application reads '{setting}' and the image never declares "
                f"'{env}'. A configuration path nothing provides does not fail - it loads as "
                "absent, and the platform carries on without whatever that configuration was for."
            )

    for env, container_path in sorted(declared.items()):
        setting = env.replace("__", ":")
        if setting not in read:
            problems.append(
                f"Dockerfile: '{env}' is declared and nothing reads '{setting}'. Either the "
                "setting was renamed and this was left behind, or it names something that no "
                "longer exists."
            )
            continue

        # Paths inside the image itself are the image's business, not the mount's.
        if container_path.startswith("/app/"):
            continue

        source = resolve(container_path, filled)
        if source is None:
            problems.append(
                f"docker-compose.yml: '{env}' names {container_path}, and nothing is mounted "
                "there. At runtime that path is empty."
            )
        elif not source.exists():
            problems.append(
                f"Dockerfile: '{env}' names {container_path}, which resolves to "
                f"{source.relative_to(ROOT).as_posix()} and does not exist. The container would "
                "start, report healthy, and have loaded nothing."
            )

    for problem in problems:
        print(f"::error file=src/Epi.Api/Dockerfile::{problem}")

    if problems:
        sys.exit(1)

    print(
        f"Every configuration path is declared, mounted and present "
        f"({len(read)} settings checked)."
    )


if __name__ == "__main__":
    main()
