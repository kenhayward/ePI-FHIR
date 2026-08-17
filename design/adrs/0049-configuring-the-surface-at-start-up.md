# ADR-049: Configuring the authoring surface at start-up, and putting it in the stack

Status: accepted
Date: 2026-08-17

Realises CAP-CFG-006 for the web tier. Corrects a decision recorded in
[ADR-037](0037-authoring-surface.md)'s implementation rather than in its text.

## Context

The authoring surface was not in the development stack. Everything else the platform needs runs
under `deploy/docker-compose`; the surface was a thing you ran with `npm run dev`, which means
nobody had ever seen the whole platform running at once.

Wiring it in forced a question its code had already answered, wrongly. `main.tsx` read its
addresses from Vite's build-time environment and said so:

> Read at build time, as a static bundle must - there is no server here to read configuration on
> start-up.

Both halves are wrong. A static bundle can fetch a file before it renders, which is what most of
them do. And "a deployment elsewhere is a different build" is the part that matters: an image with
a hostname compiled into it cannot be promoted between environments, only rebuilt for each - so
what runs in production is not the artefact CI proved. That is the same argument ADR-012 makes for
the service tier, where configuration is mounted precisely so that changing a market is not a
release.

Wiring it in also turned up two things nobody could have found by reading, which is the usual
result of running this stack.

## Decision

**1. The surface reads where it is pointed at start-up, from `config.json` served beside it.**
Nothing renders until it has been read, and an incomplete file is refused with every missing field
named at once. A surface that defaulted to localhost would fail in a deployment in a way nobody
would attribute to configuration - the defect class the service side has been bitten by three
times.

**2. The configuration is mounted, and the image carries none.** The development server's copy is
deleted during the build. An image with an address in it is one that starts happily in the wrong
environment; without a mount this one serves nothing and says why. Verified both ways: through the
stack it answers the mounted file, and run bare it answers 404 for it.

**3. The addresses in it are the browser's, not the container network's.** This is the trap the
web tier has and the service tier does not: a browser resolves `localhost` and cannot reach
`epi-api` or `keycloak` whatever the container serving the files can. It is stated in the file, in
the compose service, in the Dockerfile and in both READMEs, because nothing can check it - the
browser is outside the network any check would run in.

**4. An address is validated as one.** `URL.canParse` alone is not enough: `localhost:8080` parses
with `localhost:` as its scheme, so the check requires `http:` or `https:`. The test for this
passed against `canParse` before the protocol check was added, which is the only reason the hole
was found.

**5. Served on 5173.** That is a redirect URI the `epi-authoring-ui` client already permits, and a
port the identity provider does not permit is a surface that cannot sign in. Its other permitted
port, 8000, is Kong's under the gateway profile - not a theoretical collision, since Kong had it
on the first machine this was tried on. The cost is that `npm run dev` and the container cannot
both run, which is a choice between two things nobody runs at once.

**6. Realm drift is reported by a tool rather than left to be diagnosed.** `tools/verify-realm.py`
says what the running Keycloak lacks against the import file, and changes nothing.

## What running it found

**The example configuration pointed the surface at itself.** `apps/authoring-ui/.env.example`
carried `VITE_EPI_API=http://localhost:8000`, which is where the surface was to be *served*, not
where the API is. Nobody noticed because the surface had never been served. That file is gone; the
addresses now live in one place.

**Sign-in fails on any stack whose Keycloak volume predates the `epi-authoring-ui` client.**
Keycloak imports a realm only if it does not already exist, so the client sits in the file and not
in the volume, and the surface bounces off the identity provider with "Client not found" - which
reads like a bug in the surface. This is the second time realm drift has presented as something
else; `user-ops` missing presented as a 403 from the reconciliation report (ADR-047's pull
request). Hence decision 6.

Recreating the realm fixes it and **mints new subject identifiers**, and the platform's audit
records, signatures and pinned contexts are attributed to the old ones. Nothing reconnects them.
On a demonstration stack that is usually acceptable, and it should be a decision rather than a
discovery - so the tool says it and does not act.

## Alternatives considered

**Pass the addresses as Docker build arguments.** Keeps the existing code and makes the image
environment-specific, which is the thing decision 1 exists to avoid. It also means changing
`EPI_API_PORT` in `.env` silently serves a surface pointed at the old one until somebody
remembers to rebuild.

**Serve the surface through Kong, as the README once implied.** The right shape eventually - one
origin for the API and the surface removes the cross-origin question entirely - and it makes the
gateway profile mandatory for anybody who wants to see the application. Recorded as the direction
rather than done.

**Recreate the realm as part of this change.** Would make sign-in work immediately on the machine
it was written on, by destroying the subject identifiers every existing audit record refers to.
Not a decision to take on somebody's behalf.

## Consequences

`docker compose up -d` now brings up the whole platform including its surface, which is the first
time that has been true.

Sign-in works on a fresh volume and not on one that predates the client. That is a property of the
deployment rather than of this change, it is now reported rather than mysterious, and the remedy is
documented with its cost.

The surface still has no origin in common with the API, so it depends on the API's CORS
configuration. That has not been exercised through a browser end to end - sign-in stops first on
this machine - and it is the next thing to check on a stack whose realm matches the file.
