#!/usr/bin/env python3
"""Report what the realm import file declares that the running Keycloak does not have.

Keycloak imports a realm only if it does not already exist. Every realm change after the first
start therefore lands in the file and not in the volume, and the symptom is never "the realm is
out of date" - it is a specific thing failing for a reason nobody attributes to configuration:

  - adding the platform-operator role and user-ops (PR 89) left the reconciliation report
    answering 403 to the only identity permitted to run it;
  - adding the epi-authoring-ui client left the authoring surface bouncing off the identity
    provider with "Client not found", which reads like a bug in the surface.

Twice is a pattern, so this reads both and says what is missing. It changes nothing: recreating a
realm mints new subject identifiers, and the platform's audit records and signatures are
attributed to the old ones - so what to do about a drifted realm is a decision with consequences
and belongs to whoever owns the deployment.

Usage:

    python tools/verify-realm.py [--keycloak http://localhost:8081]

Exit codes: 0 when the running realm has everything the file declares, 1 when it does not, and
2 when the realm could not be read at all (which is a stack that is not up, not a drift).
"""
import argparse
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REALM_FILE = ROOT / "deploy" / "docker-compose" / "init" / "keycloak-epi-realm.json"


def declared():
    """What the import file says the realm should have."""
    realm = json.loads(REALM_FILE.read_text(encoding="utf-8"))

    return {
        "clients": sorted(
            client["clientId"] for client in realm.get("clients", []) if "clientId" in client),
        "realm roles": sorted(
            role["name"] for role in realm.get("roles", {}).get("realm", []) if "name" in role),
        "users": sorted(
            user["username"] for user in realm.get("users", []) if "username" in user),
    }


def present(keycloak, realm_name, clients):
    """Which of those the running Keycloak actually has.

    Read without administrator credentials, because a check that needed them would not run where
    it is most useful - on somebody else's stack, or in a walkthrough. Clients are probed through
    the authorization endpoint, which answers differently for a client that does not exist; roles
    and users are not readable anonymously and are reported as unknown rather than as missing.
    """
    found = set()

    for client in clients:
        url = (
            f"{keycloak}/realms/{realm_name}/protocol/openid-connect/auth"
            f"?client_id={urllib.parse.quote(client)}&response_type=code"
            "&redirect_uri=http%3A%2F%2Flocalhost%2F"
        )

        try:
            with urllib.request.urlopen(url, timeout=15) as response:
                body = response.read().decode(errors="replace")
        except urllib.error.HTTPError as error:
            body = error.read().decode(errors="replace")

        # A client Keycloak does not know is named as such; one it knows either shows a sign-in
        # page or complains about the redirect URI, and both mean the client is there.
        if "client not found" not in body.lower():
            found.add(client)

    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--keycloak", default="http://localhost:8081")
    parser.add_argument("--realm", default="epi")
    arguments = parser.parse_args()

    keycloak = arguments.keycloak.rstrip("/")
    wanted = declared()

    try:
        with urllib.request.urlopen(
                f"{keycloak}/realms/{arguments.realm}/.well-known/openid-configuration",
                timeout=15):
            pass
    except (urllib.error.URLError, TimeoutError, OSError) as down:
        print(f"Could not read the '{arguments.realm}' realm at {keycloak}: {down}")
        print("That is a stack that is not up rather than a realm that has drifted.")
        return 2

    have = present(keycloak, arguments.realm, wanted["clients"])
    missing = [client for client in wanted["clients"] if client not in have]

    for client in wanted["clients"]:
        print(f"  {'present' if client in have else 'MISSING'}  client {client}")

    print(
        f"\n  not checked  realm roles {', '.join(wanted['realm roles'])}"
        f"\n  not checked  users {', '.join(wanted['users'])}"
        "\n               (neither is readable without administrator credentials; a client that"
        "\n               is missing means the realm predates the file, and these will be too)")

    if not missing:
        print("\nThe running realm has every client the import file declares.")
        return 0

    print(
        f"\nThe running realm is missing: {', '.join(missing)}."
        "\n\nKeycloak imports a realm only if it does not already exist, so this volume predates"
        "\nthose entries. To take the file's realm into this stack, delete the"
        f"\n'{arguments.realm}' realm in the Keycloak admin console and restart Keycloak, or start"
        "\nfrom a fresh volume."
        "\n\nBefore doing either: recreating the realm mints NEW subject identifiers. The"
        "\nplatform's audit records, signatures and pinned contexts are attributed to the old"
        "\nones, and nothing will reconnect them. On a demonstration stack that is usually"
        "\nacceptable; decide rather than discover it.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
