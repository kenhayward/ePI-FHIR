# ADR-039: Signing in to the authoring surface

Status: accepted
Date: 2026-08-16

Realises CAP-IAM-001 in the web tier. Applies [ADR-037](0037-authoring-surface.md), whose
decision 1 keeps every control on the server and whose consequence is that the surface holds a
token and nothing else. Required before any of [ADR-038](0038-authoring-projection.md)'s
endpoints are reachable from a browser.

## Context

The authoring surface has a client that asks for an access token before every request, and
nothing supplies one. It is the last structural gap: everything else about the surface is
tested and none of it can reach the platform.

The platform never authenticates anyone itself (D3 Section 8) - Keycloak does, and the API
validates the resulting JWT. So the only question here is how a browser gets a token, and where
it keeps it.

That second half is the one worth writing down, because the convenient answer is the one that
loses.

## Decision

**1. Authorization code with PKCE, and nothing else.** A public client, no secret, no implicit
flow, no password grant. A secret shipped to a browser is not a secret; implicit puts a token in
a URL, which puts it in browser history and every referrer; and a password grant means this
application handles a password, which decision 4 of ADR-037 and the whole shape of D3 Section 8
exist to prevent.

**2. The access token lives in memory and nowhere else.** Not `localStorage`, not
`sessionStorage`, not a cookie this application sets.

This is the decision with a cost, and the cost is real: a page refresh loses the session and the
author signs in again. Storage would fix that, and it would also mean that any script that ever
executes on this origin can read a token that authorises writes to regulated content. Cross-site
scripting is the failure mode that matters here, and `localStorage` converts an XSS into a
token exfiltration; memory does not, because there is nothing to read after the tab closes.

Given a platform whose whole subject is the integrity of regulated content, an author signing in
again after a refresh is the cheaper of the two.

**3. No refresh token in the browser.** For the same reason, more so: a refresh token is a
longer-lived credential and the browser is the worst place to keep one. When the access token
expires, the author is sent back through the identity provider, which will usually return them
immediately because the Keycloak session is still valid.

**4. `state` is generated, stored and checked, and a mismatch is refused.** The cross-site
request forgery guard the flow depends on. Refused loudly rather than ignored: a callback whose
state does not match is either a defect or an attack, and neither should proceed to a token
exchange.

**5. The flow is implemented directly rather than through a library.** Stated as a deliberate
choice, because "don't roll your own auth" is usually right.

What tips it here is decision 2. The established libraries default to `localStorage` or
`sessionStorage` and to silent renewal with refresh tokens, which are exactly the two things
this record rejects - so adopting one would mean configuring against its defaults and depending
on that configuration staying right. And the part of OIDC that is genuinely dangerous to
hand-roll is *validating* a token, which happens on the API and not here: this code builds a URL,
checks a string it generated, and posts a form.

If this grows - silent renewal, multiple identity providers, session management - that reasoning
expires and a library is the right answer.

**6. The surface renders nothing about a label until there is a token.** An editor that appears
and then fails on save is worse than a sign-in prompt.

## Consequences

The Keycloak realm gains a public client for the surface with PKCE required, standard flow only,
and direct access grants disabled. It is configuration in the seeded realm, like everything else
about the development identity provider.

A page refresh signs the author out of the surface. That is decision 2's price, paid knowingly,
and it is the thing most likely to be raised as a defect by somebody who has not read this.

Nothing here addresses what the surface does when a token expires mid-edit with unsaved work.
The working copy is in memory (ADR-037 decision 6) and survives a re-authentication only if the
page is not reloaded, which a redirect does. Recorded as a debt, and it is the strongest
argument yet for the server-side draft workspace that ADR-037 already names.
