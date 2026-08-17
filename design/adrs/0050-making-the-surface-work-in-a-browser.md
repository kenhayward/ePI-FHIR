# ADR-050: Making the surface work in a browser

Status: accepted
Date: 2026-08-17

Realises CAP-IAM-001 and CAP-CFG-006. Follows [ADR-049](0049-configuring-the-surface-at-start-up.md),
which put the surface in the stack and named what it had not proved.

## Context

[ADR-049](0049-configuring-the-surface-at-start-up.md) closed by saying the surface "has not been
exercised through a browser end to end", and that the API's cross-origin configuration was the next
thing to check. Doing that found **four defects, one after another**, each hidden behind the last.

The surface had 187 passing tests, a clean type-check and a clean production build. It could not
sign in, could not reach the platform, and could not have been used by anybody.

Each defect shares a cause: **the tests exercise the surface without a browser, and a browser is
the only thing that enforces any of these rules.**

1. **`Illegal invocation`.** `this.#fetch = globalThis.fetch` stores the function unbound, and
   `this.#fetch(...)` then calls it with the surrounding object as its receiver. Chrome refuses a
   Window method called on anything else. Invisible because every test injects its own fetcher, so
   the branch taking the global one never ran - and Node's fetch does not check its receiver, so
   injecting the real one would not have caught it either.

2. **The screen never updated after signing in.** The exchange succeeded, the token sat in memory,
   and the author went on being told "You are signed out". `hasValidToken` is a getter on a mutable
   object, so nothing told React the answer had changed. The test for this asserted that
   `completeAsync` had been *called* and nothing about what the author then saw - the same shape as
   a gate whose tests only assert its refusals (ADR-047).

3. **No cross-origin headers at all.** The surface is served from one origin and the API answers on
   another. Every response arrived and was thrown away unread; every preflight got 405.

4. **The surface's identity-provider client had no protocol mappers.** No `epi-api` audience, so the
   API refused its tokens as issued to somebody else; and no roles, affiliates or markets, so every
   authorization decision would have denied for want of anything to decide on. The symptom was 401
   from every call.

## Decision

**1. A global function stored for later use is bound.** `globalThis.fetch.bind(globalThis)`, in
every place the platform client, the sign-in and the settings loader take the ambient one.

**2. A completed sign-in bumps state.** Its value means nothing; changing it is what makes React
look at `hasValidToken` again.

**3. The spent authorization code is taken out of the address, and failing to do so is not a
failed sign-in.** A code is used once (ADR-039 decision 4), so leaving it there means a refresh
replays a code the identity provider has consumed and the author is refused for pressing F5. The
tidying is wrapped separately: some environments refuse `replaceState`, and reporting that as a
problem would tell an author their sign-in failed when it had just succeeded.

**4. Browser origins are configuration, and `*` is refused at start-up.** `Epi:Cors:Origins`, named
origins only. Any origin would make the platform readable by every page a signed-in author happens
to visit, and access control would rest entirely on a token nobody had stolen yet. `*` is what
somebody reaches for when a browser refuses them, so it fails loudly where it is a decision to
correct. A deployment configuring none allows none, and says so at start-up - an API that allowed
localhost by default would be a production deployment answering a developer's machine.

**5. CORS is applied before authentication.** A preflight carries no token: the browser is asking
whether it may send the Authorization header at all, and a 401 to that question is a call that
never happens.

**6. The surface's client carries the same mappers the signing client does.** Audience, roles,
affiliates, markets. Not "similar" - the same four, because a token missing any of them fails in a
different place and each failure looks like a different bug.

## Consequences

The application works, verified in a browser rather than asserted: signed in, searched and got 105
scoped results with real states, opened an approved version, and saw its filed leaflet in the
sandboxed frame with no preview panel beside it - ADR-048's exclusivity holding on a real screen.

Three new test files cover what a browser enforces, using stand-ins that behave as a browser does
rather than as Node does. The one for `fetch` is worth describing: it installs a fetch that refuses
a wrong receiver exactly as Chrome's does, so anything storing an unbound global fails it. Node's
own fetch cannot express that difference, which is why the defect survived.

Adding the mappers is a realm change, and Keycloak imports a realm only if it does not already
exist (ADR-049). An existing volume needs them applied through the admin API or the realm
recreated; `tools/verify-realm.py` reports client presence but cannot see mappers, which is a gap
it should close.

Deep-linking to a label does not survive signing in. The redirect URI is the origin root, so the
`?label=` the author arrived with is dropped and they land on the picker. A consequence of
ADR-039's in-memory token rather than of anything here, and worth fixing when somebody needs to
share a link.

A fifth thing surfaced while verifying and is not a defect in the repository: OPA loads
`policies/` at start-up, so a role added after the container started is absent from the running
decision point, and the symptom is a 403 with a correct-looking token. It is the same shape as the
Keycloak realm trap and it is now in the compose README beside it. The walkthrough passes in full
once the decision point has been restarted - the first time it has, in this pull request's session.

The lesson generalises past this pull request: **a surface tested only without a browser is a
surface nobody has run.** Every one of these four passed every test that existed.
