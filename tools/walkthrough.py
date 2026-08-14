"""Walks the governed flow against the running development stack.

Run from the repository root, with the stack up:

    cd deploy/docker-compose && docker compose up -d
    python tools/walkthrough.py

This is not a substitute for the test suite, which proves each part in isolation and runs in
CI. It exercises the seams the suite cannot reach - the issuer both sides must agree on, the
paths a container resolves differently from a checkout, the policy the decision point is
actually loaded with - and every one of those has been broken at least once.
"""
import base64
import json
import urllib.error
import urllib.parse
import urllib.request

KEYCLOAK = "http://localhost:8081"
API = "http://localhost:8080"
PASSWORD = "Demo-Passw0rd!"
FIXTURE = "tests/fixtures/epi/minimal-epi-document.json"

failures = []


def check(label, condition, detail=""):
    print(f"  {'PASS' if condition else 'FAIL'}  {label}{(' - ' + detail) if detail else ''}")
    if not condition:
        failures.append(label)


def token(user):
    data = urllib.parse.urlencode({
        "grant_type": "password", "client_id": "epi-signing",
        "username": user, "password": PASSWORD, "scope": "openid profile"}).encode()
    request = urllib.request.Request(
        f"{KEYCLOAK}/realms/epi/protocol/openid-connect/token", data=data)
    with urllib.request.urlopen(request, timeout=15) as response:
        return json.load(response)["access_token"]


def call(method, path, jwt, body=None, content_type="application/json"):
    data = None if body is None else (
        body.encode() if isinstance(body, str) else json.dumps(body).encode())
    request = urllib.request.Request(f"{API}{path}", data=data, method=method)
    request.add_header("Authorization", f"Bearer {jwt}")
    if data is not None:
        request.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read().decode()
            return response.status, (json.loads(raw) if raw.strip().startswith(("{", "[")) else raw)
    except urllib.error.HTTPError as error:
        raw = error.read().decode()
        return error.code, (json.loads(raw) if raw.strip().startswith(("{", "[")) else raw)


def scoped_document():
    """The fixture, tagged with the affiliate and market the demonstration users hold."""
    bundle = json.load(open(FIXTURE, encoding="utf-8"))
    meta = bundle.setdefault("meta", {})
    tags = meta.setdefault("tag", [])
    tags.append({"system": "https://epi.example.org/tag/affiliate", "code": "uk-affiliate"})
    tags.append({"system": "https://epi.example.org/tag/market", "code": "GB"})
    return json.dumps(bundle)


print("Signing in")
anna, ben = token("user-anna"), token("user-ben")
check("Anna and Ben hold tokens", bool(anna) and bool(ben))

print("\nAnna creates a label")
status, created = call("POST", "/fhir/Bundle", anna, scoped_document(), "application/fhir+json")
check("content is created", status == 201, f"{status} {str(created)[:160]}")
if status != 201:
    raise SystemExit("cannot continue: " + str(created)[:400])

identifier = created["identifier"]
print(f"  identifier {identifier}")

status, state = call("GET", f"/labels/{identifier}/versions/1/state", anna)
check("it is a draft, attributed to a stable subject id",
      status == 200 and state["state"] == "draft" and len(state["author"]) > 20,
      f"{status} {state}")
check("markets are reported separately",
      status == 200 and state["markets"] == {"GB": "not-submitted", "EU": "not-submitted"},
      str(state.get("markets") if status == 200 else state))

print("\nAnna submits it for review")
status, moved = call("POST", f"/labels/{identifier}/versions/1/transitions", anna,
                     {"action": "submit", "reason": "ready for review"})
check("submitted", status == 200 and moved["to"] == "in-review", f"{status} {moved}")

print("\nAnna tries to approve her own work")
status, signed = call("POST", "/signatures", anna,
                      {"documentIdentifier": identifier, "version": 1,
                       "meaning": "Approval", "password": PASSWORD, "reason": "self"})
check("Anna can sign (signing is not the control)", status == 200, str(status))
if status == 200:
    status, refused = call("POST", f"/labels/{identifier}/versions/1/transitions", anna,
                           {"action": "approve", "signatureReference": signed["reference"]})
    check("but the author may not approve it", status == 409,
          f"{status} {str(refused)[:200]}")

print("\nBen signs with a wrong password")
status, refused = call("POST", "/signatures", ben,
                       {"documentIdentifier": identifier, "version": 1,
                        "meaning": "Approval", "password": "not-bens-password"})
check("refused", status == 403, str(status))

print("\nBen signs properly and approves")
status, signature = call("POST", "/signatures", ben,
                         {"documentIdentifier": identifier, "version": 1, "meaning": "Approval",
                          "password": PASSWORD, "reason": "checked against source"})
check("signed", status == 200, f"{status} {str(signature)[:200]}")
if status == 200:
    check("the manifest attributes to a subject, prints a name, and hashes the version",
          len(signature["signer"]) > 20
          and signature["printedName"] == "Ben Okafor"
          and signature["contentHash"].startswith("sha-256:"),
          f"{signature.get('printedName')} {signature.get('contentHash', '')[:24]}")

    status, approved = call("POST", f"/labels/{identifier}/versions/1/transitions", ben,
                            {"action": "approve", "signatureReference": signature["reference"]})
    check("approved", status == 200 and approved["to"] == "approved", f"{status} {approved}")

    print("\nBen tries to spend the same signature again")
    status, again = call("POST", f"/labels/{identifier}/versions/1/transitions", ben,
                         {"action": "withdraw", "signatureReference": signature["reference"]})
    check("refused as already used", status == 409, f"{status} {str(again)[:200]}")

print("\nFinal state")
status, state = call("GET", f"/labels/{identifier}/versions/1/state", anna)
check("approved internally, still unsubmitted in every market",
      status == 200 and state["state"] == "approved"
      and set(state["markets"].values()) == {"not-submitted"},
      f"{status} {state}")

print("\n" + ("ALL CHECKS PASSED" if not failures else f"FAILURES: {failures}"))
