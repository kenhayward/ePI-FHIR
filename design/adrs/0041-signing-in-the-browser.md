# ADR-041: Signing a record in the browser

Status: accepted
Date: 2026-08-16

Realises CAP-WFL-003 and CAP-AUD-004 in the web tier. Applies
[ADR-020](0020-electronic-signature.md), which made an approval gate require a signature over the
content hash, and **corrects a statement in [ADR-039](0039-authoring-sign-in.md)** that was true
of authentication and stated as though it were true of everything.

## Context

The surface can now show somebody that they have been asked to approve a version. Acting on that
ask means passing a signed gate, and the platform's signature endpoint takes a password: the
signer re-enters their credentials at the moment of signing, and the platform verifies them
against the identity provider (ADR-020, `ICredentialVerifier`).

ADR-039 said this application never asks for a password, and a test on the sign-in screen asserts
there is no password field. Both are right about **authentication** and neither is right about
**signing**, which this record has to separate before a password box appears anywhere.

## Decision

**1. Authentication and signing are different acts, with different rules.** Signing in is
delegated to the identity provider and this application never sees a credential - that is
ADR-039 decision 1 and it is unchanged. Signing a *record* is an assertion by a named person
about a specific artefact, and 21 CFR Part 11 requires it to use identification components the
signer supplies at the time.

Re-entering a password is not a weaker form of signing in. It is the control: it is what makes
the signature attributable to a person rather than to a session somebody left open.

**2. The password exists for one request and is never anywhere else.** Not in component state
beyond the moment, not in a form the browser will offer to remember, not in any store, not in a
log, and never in a URL. It is read, sent to the platform's signature endpoint, and dropped.

**3. It is sent to the platform, never to the identity provider.** The platform verifies it
(ADR-020) and returns a signature reference. A browser posting credentials directly to Keycloak
would be a second authentication path with none of the platform's segregation-of-duties checks
around it.

**4. The signature reference is what passes the gate, and the surface never mints one.** It asks
for a signature, gets a reference, and cites it on the transition. Whether the reference is
valid, unspent and over the right content hash is the platform's to decide, and it refuses the
transition if not (ADR-020) - so the surface can be wrong about it and the gate still holds.

**5. The correction, stated plainly.** ADR-039 decision 1's reasoning is sound and its wording
over-reached. "This application never asks for a password" is true of sign-in and false of
signing, and the sign-in screen's test asserting no password field stays exactly as it is - it
is asserting the right thing about the right screen.

## Alternatives considered

**Step up through the identity provider instead**, re-authenticating with `prompt=login` and
`max_age=0`, and treating the fresh token as the signing evidence. **This is the better answer**
and it is not the one taken here.

It is better because the surface would never handle a credential at all, and because an identity
provider can require a second factor for the step-up - which is what a serious Part 11 posture
looks like. It is not taken here because the platform's signature service verifies a password
today (ADR-020), so adopting it means changing what a signature *is* on the server first. That
is a change to a signed, audited control and it deserves its own decision rather than arriving as
a consequence of building a screen.

Recorded as the intended direction, with the reason it is not yet done.

**Sign without re-authentication, on the session alone.** Rejected: it makes the signature an
assertion by a browser tab. Part 11 aside, it is the difference between a signature and a click.

## Consequences

A password field exists in this application, on one screen, for one purpose. It is the kind of
thing that gets copied, so it carries a comment saying why it is there and why it is not a
sign-in.

`autoComplete="off"` and `type="password"` are necessary and not sufficient - a browser or
password manager may still offer to store it. That is outside this application's control and
worth knowing rather than pretending otherwise.

Decision 4 means the surface can hold a stale or spent reference and simply be refused. That is
the intended failure: the reference is evidence, the gate is the control, and they are checked in
different places.
