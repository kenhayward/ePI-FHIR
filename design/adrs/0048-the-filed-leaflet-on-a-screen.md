# ADR-048: The filed leaflet on a screen

Status: accepted
Date: 2026-08-17

Realises CAP-RND-002 and CAP-RND-004 on the authoring surface. Follows
[ADR-046](0046-official-render.md), which gave the platform an official render, and
[ADR-037](0037-authoring-surface.md), which says what a surface may and may not decide.

## Context

The surface had one leaflet panel, and it was a preview that said so - correctly, because nothing
could produce anything else. [ADR-046](0046-official-render.md) changed that: an approved version
and an approved template now produce the artefact of record, filed under object-lock and citable
by key.

Putting it on a screen raises a question the API did not have to answer. Over HTTP the two are
different endpoints and nobody confuses them. On a screen they are two rectangles of rendered
HTML that look almost identical, and CAP-RND-004 exists because somebody who cannot tell them
apart eventually sends the wrong one.

## Decision

**1. One panel at a time, chosen by the version's state.** An approved version shows the filed
leaflet; anything else shows the preview. Never both.

Showing both would be the more informative screen and the less safe one. The preview's honest
caption - "this is not the artefact that would be filed" - stops being reassuring the moment the
artefact is on the same page: a reader has to work out which rectangle the caption belongs to.
Exclusivity means the caption is never ambiguous, because there is only ever one thing to read.

**2. Producing one is a button.** It files something. A screen that filed an artefact as a side
effect of being opened would be filing on somebody's behalf, and the record would attribute it to
whoever happened to navigate there.

**3. Only approved templates are offered.** Filtered before the list reaches the screen, not
displayed and then refused. A draft template cannot produce an official render (ADR-042 decision
4), so offering one offers a choice the platform will turn down - and an author would reasonably
conclude the platform was broken rather than that they had picked an unapproved template.

**4. A deployment with no approved template says so, and offers no button.** That is the state a
fresh deployment is in (ADR-042 decision 7), and the remedy is somebody approving a template
rather than anything an author can do on this screen.

**5. The key is shown.** The artefact is citable by it, and one somebody can see but not refer to
is one they cannot point a regulator at.

**6. "Already filed" is said, and distinguished from "filed".** A render is a pure function of its
two versions, so a second request returns the first one's bytes (ADR-046 decision 4). Saying
"filed" for both would tell an author they had just produced something they had not.

**7. Refusals show what the platform said.** Which rule refused is the only thing that tells
somebody what to do next: get the version approved, get a template approved, or look at a version
that exists.

**8. The artefact goes in a sandboxed frame, never into the page.** The same rule as the preview
(ADR-037), for the same reason: it is the platform's own output and it is still a document
assembled from content people type, and this page's origin, session and token are none of its
business.

## Alternatives considered

**One panel with a toggle between preview and filed artefact.** More flexible, and it makes the
confusion a click away rather than impossible. A toggle also implies the two are the same kind of
thing seen differently, which is the opposite of what CAP-RND-004 says.

**Produce the artefact automatically when an approved version is opened.** Fewer clicks and it
files on somebody's behalf, attributing the act to whoever navigated there. Approval is a
decision; filing its artefact is a consequence somebody should still choose.

**Offer every template and let the platform refuse.** Simpler client, and it teaches authors that
the platform refuses things at random. Filtering to approved templates is not hiding a capability -
it is not offering one that does not exist.

## Consequences

An approved version now has a screen showing the document a regulator would receive, produced on
request and filed where it can be cited. That is the first time this platform has shown anybody
the artefact of record rather than a rehearsal of it.

The preview panel is unchanged and its caption is now true in a stronger sense: there *is* a
different thing, and it is one state away.

The surface still cannot author a template - it renders with ones somebody else approved. ADR-042's
consequence stands: the preview scaffolding goes when the surface can author templates, and its
removal is what will say this is finished rather than merely built.

An approved version's panel offers a choice of template with no guidance on which to pick. A
person who knows their labelling knows; a platform that offered a default would be making an
editorial decision it has no basis for (ADR-037). Recorded because it will look like an omission
to anybody who has not read this paragraph.
