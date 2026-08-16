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


def call(method, path, jwt, body=None, content_type="application/json", base=None):
    """Calls the API, or another service where one is named - the identity provider has
    endpoints the surface depends on and the API knows nothing about."""
    data = None if body is None else (
        body.encode() if isinstance(body, str) else json.dumps(body).encode())
    request = urllib.request.Request(f"{base or API}{path}", data=data, method=method)
    if jwt is not None:
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
# Every configured market, and nothing assumed about which they are: a market is added by
# dropping a file into config/markets, so a walkthrough naming them is a walkthrough that
# fails the next time somebody does. It went stale exactly that way when Germany was added.
check("every configured market is reported separately, and none has been submitted to",
      status == 200 and len(state["markets"]) >= 2
      and set(state["markets"].values()) == {"not-submitted"},
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

# ---------------------------------------------------------------------------
# The authoring projection (ADR-038), which the surface is the only caller of.
#
# Every part of it is unit-tested against fakes. What no test reaches is the round trip through
# a real HAPI FHIR store: whether the section identities a real Bundle carries survive being
# projected and patched back, and whether a save through this path lands as an ordinary version.

print("\nAnna opens the label as sections, and edits one")
status, view = call("GET", f"/labels/{identifier}/versions/1/sections", anna)
check("a version reads as sections rather than as a Bundle",
      status == 200 and "sections" in view and "entry" not in str(view),
      f"{status} {str(view)[:120]}")

if status == 200 and view.get("sections"):
    first = view["sections"][0]
    check("each section carries the identity the platform assigned",
          bool(first.get("identity")) and bool(first.get("title")),
          f"{first.get('identity')} {first.get('title')}")

    # Approved, and still editable: saving mints the next version rather than changing this one,
    # which is what ADR-038 decision 6 corrected ADR-037 about.
    check("an approved version is still editable, because saving drafts the next one",
          view.get("editable") is True and view.get("state") == "approved",
          f"editable={view.get('editable')} state={view.get('state')}")

    edited = dict(first)
    edited["narrative"] = ('<div xmlns="http://www.w3.org/1999/xhtml"><p>SYNTHETIC - rewritten '
                           "by the walkthrough.</p></div>")

    status, saved = call(
        "POST", f"/labels/{identifier}/versions/1/sections", anna, {"sections": [edited]})
    check("saving sections mints the next version", status == 201 and saved.get("version") == 2,
          f"{status} {saved}")

    status, reread = call("GET", f"/labels/{identifier}/versions/1/sections", anna)
    check("the version that was read is untouched",
          status == 200 and "rewritten by the walkthrough" not in str(reread), str(status))

    status, later = call("GET", f"/labels/{identifier}/versions/2/sections", anna)
    check("the new version carries the edit",
          status == 200 and "rewritten by the walkthrough" in str(later), str(status))

    if status == 200:
        # The one a fake cannot answer: a real Bundle round-tripped through HAPI FHIR, projected
        # and patched, must keep the identities that make a save addressable at all.
        check("section identities survive the round trip through the content store",
              [s["identity"] for s in later["sections"]]
              == [s["identity"] for s in view["sections"]],
              str([s["identity"] for s in later["sections"]][:2]))

        check("everything the author did not touch is unchanged",
              len(later["sections"]) == len(view["sections"])
              and later["sections"][-1]["narrative"] == view["sections"][-1]["narrative"],
              f"{len(later['sections'])} of {len(view['sections'])} sections")

    status, refused = call(
        "POST", f"/labels/{identifier}/versions/2/sections", anna,
        {"sections": [{"identity": "sec-invented", "title": "Invented",
                       "narrative": '<div xmlns="http://www.w3.org/1999/xhtml"><p>x</p></div>'}]})
    check("a save naming a section the version does not have is refused",
          status == 400, f"{status} {str(refused)[:120]}")

print("\nThe surface's own client is registered with the identity provider")
status, realm = call(
    "GET", "/realms/epi/.well-known/openid-configuration", None, base=KEYCLOAK)
check("the realm advertises the authorization endpoint the surface redirects to",
      status == 200 and "authorization_endpoint" in realm, str(status))

# ---------------------------------------------------------------------------
# What the authoring surface reads, in the shape it actually arrives in.
#
# Every part of the surface is tested against fakes, and a fake is written from what the
# component wants rather than from what the platform sends. That gap has already produced one
# defect - a market rendered with no state at all, because the fake returned the joined shape
# the screen wanted and the platform answers two fields. These checks are the shape itself.

print("\nThe surface reads what may be done, rather than working it out")
status, view = call("GET", f"/labels/{identifier}/versions/2/sections", anna)
check("the projection says what the state model permits from here",
      status == 200 and isinstance(view.get("actions"), list) and len(view["actions"]) > 0,
      f"{status} {view.get('actions')}")
check("and which of those the platform will require a signature for",
      status == 200 and isinstance(view.get("signedActions"), list),
      str(view.get("signedActions")))

status, state = call("GET", f"/labels/{identifier}/versions/2/state", anna)
check("market states are still a map of market to state, as callers already read them",
      status == 200 and all(isinstance(v, str) for v in state.get("markets", {}).values()),
      str(state.get("markets"))[:120])
check("what may be done per market is a second field beside it",
      status == 200 and isinstance(state.get("marketActions"), dict)
      and set(state["marketActions"]) == set(state["markets"]),
      str(sorted(state.get("marketActions", {})))[:120])

if status == 200 and state.get("marketActions"):
    # The distinction the whole market model exists around (CAP-LCM-012), asserted against the
    # configured model rather than against a fixture that agrees with itself.
    not_submitted = [m for m, s in state["markets"].items() if s == "not-submitted"]
    if not_submitted:
        actions = state["marketActions"][not_submitted[0]]
        check("submitting to a regulator is a signed act",
              "submit" in actions.get("signedActions", []), str(actions.get("signedActions")))
        check("and nothing at not-submitted asks for an effective date",
              actions.get("actionsNeedingEffectiveDate") == [],
              str(actions.get("actionsNeedingEffectiveDate")))

print("\nAnna says which product the label is about")
status, products = call("GET", "/master-data/products?text=examplinum", anna)
check("the product directory answers over HTTP", status == 200 and len(products) > 0,
      f"{status} {str(products)[:100]}")
check("an empty query is refused rather than listing the catalogue",
      call("GET", "/master-data/products", anna)[0] == 400, "")

if status == 200 and products:
    chosen = products[0]
    status, view = call("GET", f"/labels/{identifier}/versions/2/sections", anna)
    status, saved = call(
        "POST", f"/labels/{identifier}/versions/2/sections", anna,
        {"sections": view["sections"],
         "product": {"identifier": chosen["identifier"], "display": chosen["name"]}})
    check("a save can say which product the label is about", status == 201, f"{status} {saved}")

    status, after = call("GET", f"/labels/{identifier}/versions/3/sections", anna)
    check("and the version comes back naming it resolvably",
          status == 200 and after.get("product", {}).get("identifier") == chosen["identifier"],
          str(after.get("product")))

    # ADR-040 decision 3: the whole reason the reference exists rather than a typed name.
    status, found = call(
        "GET", f"/labels/search?productIdentifier={chosen['identifier']}", anna)
    check("which labels are about this product is now a query",
          status == 200 and found.get("total", 0) >= 1, f"{status} total={found.get('total')}")

    status, unmatched = call("GET", "/labels/search?productIdentifier=PROD-does-not-exist", anna)
    check("and a product nothing is about finds nothing, rather than everything",
          status == 200 and unmatched.get("total") == 0, str(unmatched.get("total")))

    status, unchanged = call(
        "POST", f"/labels/{identifier}/versions/3/sections", anna, {"sections": after["sections"]})
    check("a save mentioning no product is not a save removing one", status == 201, str(status))
    status, still = call("GET", f"/labels/{identifier}/versions/4/sections", anna)
    check("the product survives a save that did not mention it",
          status == 200 and still.get("product", {}).get("identifier") == chosen["identifier"],
          str(still.get("product")))

print("\n" + ("ALL CHECKS PASSED" if not failures else f"FAILURES: {failures}"))
