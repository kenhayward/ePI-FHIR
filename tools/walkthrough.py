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
from datetime import datetime, timedelta, timezone
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

print("\nBen sees what is waiting for him")
status, waiting = call("GET", "/tasks", ben)
check("submitting for review asked an approver to approve",
      status == 200 and any(t["documentIdentifier"] == identifier and t["action"] == "approve"
                            for t in waiting),
      f"{status} {str(waiting)[:160]}")

status, annas = call("GET", "/tasks", anna)
check("and it is not waiting for the author who wrote it", annas == [], str(annas)[:120])


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

print("\nThe task Ben answered is no longer waiting")
status, remaining = call("GET", "/tasks", ben)
check("approving closed the task that asked for it", remaining == [], str(remaining)[:120])


print("\nAnna searches for it")
status, results = call("GET", "/labels/search?state=approved", anna)
check("search finds the approved label", status == 200
      and any(hit["documentIdentifier"] == identifier for hit in results.get("hits", [])),
      f"{status} {str(results)[:200]}")

status, results = call("GET", "/labels/search?market=EU", anna)
check("a market Anna does not hold returns nothing, and no count either",
      status == 200 and results["total"] == 0 and results["hits"] == [],
      f"{status} {str(results)[:200]}")

status, results = call("GET", "/labels/search?text=Examplinum", anna)
check("free text matches the narrative", status == 200 and results["total"] >= 1,
      f"{status} {str(results)[:200]}")

print("\nAnna tries to deal with the regulator herself")
status, refused = call("POST", f"/labels/{identifier}/versions/1/markets/GB/transitions", anna,
                       {"action": "submit", "reason": "not mine to make"})
check("an author may read the label and may not submit it", status == 403, str(status))

print("\nRae submits it to the Great Britain regulator and records the decision")
rae = token("user-rae")
status, unapproved = call("GET", f"/labels/{identifier}/current-approved?market=GB", rae)
check("no market has approved anything yet", status == 404, str(status))

status, signature = call("POST", "/signatures", rae,
                         {"documentIdentifier": identifier, "version": 1,
                          "meaning": "Responsibility", "password": PASSWORD,
                          "reason": "submitting to the regulator"})
check("Rae signs for the submission", status == 200, f"{status} {str(signature)[:200]}")

if status == 200:
    market_path = f"/labels/{identifier}/versions/1/markets/GB/transitions"
    status, submitted = call("POST", market_path, rae,
                             {"action": "submit", "reason": "initial submission",
                              "signatureReference": signature["reference"]})
    check("submitted under signature", status == 200 and submitted["to"] == "submitted",
          f"{status} {str(submitted)[:200]}")

    status, assessing = call("POST", market_path, rae, {"action": "begin-assessment"})
    check("under assessment", status == 200 and assessing["to"] == "under-assessment",
          f"{status} {str(assessing)[:200]}")

    # Unsigned on purpose: recording what a regulator decided is a factual entry about an
    # event outside this organisation's control, not an act of it (CAP-LCM-012, ADR-020).
    # It must say when the approval takes effect, though: there is no default (ADR-029).
    status, refused = call("POST", market_path, rae, {"action": "record-approval"})
    check("an approval that does not say when it takes effect is refused",
          status == 409, str(status))

    effective = (datetime.now(timezone.utc) + timedelta(days=30)).isoformat()
    status, decision = call("POST", market_path, rae,
                            {"action": "record-approval", "effectiveFrom": effective})
    check("the regulator's decision is recorded without a signature",
          status == 200 and decision["to"] == "approved", f"{status} {str(decision)[:200]}")

    status, current = call("GET", f"/labels/{identifier}/current-approved?market=GB", rae)
    check("Great Britain now has a current-approved version",
          status == 200 and current["version"] == 1, f"{status} {str(current)[:200]}")

    status, elsewhere = call("GET", f"/labels/{identifier}/current-approved?market=EU", rae)
    check("the European Union has not, on the same content", status == 404, str(status))

    status, unseen = call("GET", f"/labels/{identifier}/current-approved?market=EU", anna)
    check("and a caller outside that market is told nothing either", status == 404, str(status))

print("\nAnna reconstructs the approved version")
status, record = call("GET", f"/labels/{identifier}/versions/1/reconstruction", anna)
check("the reconstruction records what the version was approved against",
      status == 200 and record.get("pinnedContext") is not None
      and any(p["name"] == "hl7.fhir.uv.emedicinal-product-info"
              for p in record["pinnedContext"]["packages"]),
      f"{status} {str(record)[:200]}")

if status == 200 and record.get("pinnedContext"):
    check("the pinned packages are still the bytes recorded then",
          record["packagesStillMatch"], str(record.get("packageDiscrepancies")))
    approval = next((h for h in record["history"] if h["action"] == "approve"), None)
    check("the signature that opened the approval gate is named in the history",
          approval is not None and approval.get("signature") is not None,
          str([h["action"] for h in record["history"]]))
    if approval and approval.get("signature"):
        check("what was signed is what was pinned",
              record["pinnedContext"]["contentHash"] == approval["signature"]["contentHash"],
              record["pinnedContext"]["contentHash"][:24])

status, historical = call("GET", f"/fhir/Bundle/{identifier}/versions/1", anna)
check("the historical content itself is retrievable by version",
      status == 200 and "Examplinum" in str(historical), str(status))

status, before = call(
    "GET", f"/labels/{identifier}/versions/1/state?asAt=2020-01-01T00:00:00Z", anna)
check("and before the version existed it was in no state at all", status == 404, str(status))

print("\n" + ("ALL CHECKS PASSED" if not failures else f"FAILURES: {failures}"))
