# ADR-051: The address is where you are

Status: accepted
Date: 2026-08-17

Realises CAP-IAM-001 and the authoring surface's part of CAP-SCH-001. Closes the deep-link gap
[ADR-050](0050-making-the-surface-work-in-a-browser.md) recorded and did not fix.

## Context

Two things a browser made obvious, and no test had noticed.

**Opening a label left the address at the root.** The picker's choice was held in component state,
so the label an author was working on appeared nowhere in the address: it could not be bookmarked,
could not be sent to a colleague, and a refresh took them back to the search box. For a platform
whose whole subject is documents people review together, an unaddressable document is a gap rather
than a rough edge.

**A label address did not survive signing in.** The redirect URI is the origin, so the query was
gone by the time the identity provider sent the author back. Somebody following a link from an
email arrived at a search box with no indication of which label they had been sent to - and no way
to find out.

## Decision

**1. The address is the single answer to "which label is open".** Held in state, initialised from
the address the application loaded with, and updated by the two things that change it: opening a
label, and the browser's own navigation. The separate note of what had been picked is gone - two
sources for one question is how they disagree.

**2. Opening a label pushes an address.** Pushed rather than replaced, so back means what it means
everywhere else. If the push is refused the label still opens: declining to open a label because
the address bar could not be updated would be losing the thing for the label on it.

**3. `popstate` is followed.** The browser's back and forward buttons change the address underneath
the page and tell nobody. Without listening, back left the application entirely and the author lost
their place.

**4. Where the author was going is remembered locally, not put in the redirect URI.** Both work.
Putting the label in the redirect URI would also write a label identifier into the identity
provider's logs, and which labels somebody is reading is the platform's business, not its.

**5. A remembered address is consumed as it is read, and only on the way back from the provider.**
Left behind it would reopen weeks-old work on the next sign-in, and on a shared machine it would
reopen somebody else's label. A plain visit to the root is somebody choosing to start at the
picker; reopening whatever they last had would be overriding that choice.

## What verifying it found

**A defect in this change, found in a browser.** Tidying the spent authorization code out of the
address (ADR-050 decision 3) rewrote the address bar and left the component holding the callback's
parameters - so the next address it pushed put `code` and `state` back, and the result was a
bookmarkable address with a spent code in it that a refresh would replay. The state and the bar are
now updated together.

This is the third time in two pull requests that the fix has been correct and its interaction with
the surrounding state has not. Each was found by opening the application, and none by a test that
existed first.

## Alternatives considered

**A routing library.** Correct at four screens and more than is needed at two. The whole of this is
one piece of state, one push, and one event listener; a router would bring a dependency, a
vocabulary and its own opinions about data loading in exchange for removing about fifteen lines.
Worth revisiting when the surface has enough screens to justify one.

**Register a wildcard redirect URI and let the provider return to the full address.** Fewer moving
parts than remembering it locally, and rejected under decision 4: it puts label identifiers in
somebody else's logs. It also needs the redirect URI list widened, which is a realm change and
therefore a recreation on an existing volume (ADR-049).

**Keep the picked label in state and add a share button.** Solves sharing and not bookmarking, not
reloading, and not the back button - three of the four things people expect an address to do.

## Consequences

An author can send somebody a link to a label and the recipient lands on it, signing in on the way
if they need to. A refresh keeps them where they were. Back returns to the search they came from.

The surface still has one address for one label version, and no address for "the search I ran".
Searching does not appear in the address, so a search cannot be shared and back from a label
returns to an empty search box rather than to the results. The next thing this owes, and smaller
than it sounds now that the address is the state.

`admin` / `admin` is Keycloak's own administrator in the `master` realm and is not a user of the
platform. Signing in to the surface with it fails correctly and reads like a broken deployment; the
distinction is now stated in both READMEs, because it cost somebody a confusing five minutes.
