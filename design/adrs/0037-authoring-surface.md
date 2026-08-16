# ADR-037: The authoring surface

Status: accepted
Date: 2026-08-16

Realises CAP-TPL-005 in part, and pays debts recorded against
[ADR-028](0028-cross-references.md) and [ADR-036](0036-master-data-and-terminology-binding.md).
Applies [ADR-002](../D3-technical-architecture.md) (D3 Section 14), which chose TypeScript and
React for the web tier. The first item of iteration 4, committed as such in
[iteration-3.md](../iteration-3.md) Section 4.3 after being deferred twice.

## Context

Everything built so far is correct and reaches a regulatory affairs professional as JSON. The
platform can govern a label, translate it, route it, render it and prove what it was approved
against, and nobody can write one without posting a FHIR Bundle.

Two debts already point here and cannot be paid anywhere else.

**Cross-reference anchors** (ADR-028) are written by whatever produces the content, because
there is nothing an author uses to insert one. An author writing an anchor by hand would be
writing section identifiers by hand, which is not authoring, it is transcription with a worse
error rate.

**Product references** (ADR-036) have a directory that can answer and nothing that asks it.
`Composition.subject.display` is still a string somebody typed, because typing is the only
option a JSON payload offers.

Iteration 3 deferred this a second time on the argument that a rendered leaflet demonstrates
more than an editor over a single-language label with no reuse. That argument has expired: reuse,
translation, rendering and routing all exist now, and every one of them is easier to show being
*used* than being described.

## Decision

**1. The surface holds no governance logic, and every control stays on the server.** Validation,
segregation of duties, permitted transitions, scope, signature requirements and immutability are
decided by the platform and reflected by the surface. Nothing is enforced here.

This is the decision the rest depend on. In a regulated system a control implemented in a
browser is not a control: it is advice to a cooperative user, removable with the developer
tools, absent entirely for anything that calls the API directly. A surface that decided anything
would be a second gate, weaker than the first, and the two would drift.

The consequence is that the surface may *disable* an action, and the platform must still refuse
it. Wherever those two disagree, the platform is right and the surface has a defect.

**2. An author edits sections, never FHIR.** The editing model is the template's sections - a
title and its narrative - and the surface never shows a Bundle, a resource type or a canonical
URL. `apps/README.md` has said this since before there was anything to say it about, and it is
what makes the surface usable by the people the platform is for.

**3. No identifier is ever typed.** Section identity, cross-reference targets, reusable-unit
references and product references are all chosen from something the platform resolves. This is
the whole of the ADR-028 and ADR-036 debts: the author picks the section they mean, and the
surface writes the identifier.

**4. The narrative an author can produce is bounded to what validates.** ePI narrative is XHTML,
and a rich-text editor that can emit arbitrary HTML produces content that fails at the write gate
after the author has finished writing it. The permitted formatting is a small declared set -
paragraphs, emphasis, lists, and the anchors decision 3 inserts - and the surface cannot produce
anything outside it.

Bounded at the point of authoring rather than corrected at save, because a save that silently
rewrote what somebody wrote is worse than one that refuses: in this domain the exact words are
the point.

**5. An approved version is not editable, and the surface does not offer an editor for one.**
Immutability is the platform's (ADR-019), and presenting a text box that will certainly fail on
save is a way of teaching people that the platform is unreliable.

**6. A working copy is not a version.** Versions are immutable and minted on write, so anything
that saved as the author typed would mint hundreds of them. The working copy is held by the
surface until the author saves, and saving is what creates a version.

This is the weakest part of this record and is stated plainly rather than dressed up: it means
unsaved work lives in one browser. The proper answer is a draft workspace on the server that is
explicitly not a version, which is a content-model change rather than a surface one. Recorded as
a debt, with the shape of the answer named so it is not rediscovered.

**7. The surface is a separate application, not a page served by the API.** It builds to static
files behind the gateway (D3 Section 12), and it talks to the same public API as any other
client. Anything the surface needs that the API cannot give it is a gap in the API, and finding
those is a large part of what building this is worth.

## Alternatives considered

**A server-rendered surface, avoiding a JavaScript toolchain entirely.** Tempting: the repository
has no Node build today, and adding one brings a lockfile, a dependency surface and a second
licence regime to keep clean. Rejected because ADR-002 already chose React and because guided
authoring is interactive in a way that a form post is not - choosing a cross-reference target
means searching sections while writing a sentence.

**Generating the surface from the template definition.** Attractive and premature. The template
model exists (ADR-021) and nothing yet knows which of its sections are free text, which are
coded, and which repeat. Building a generic renderer over a model that has not been asked to
carry that would fix the wrong shape early. The surface reads the template's sections and
nothing more.

## Consequences

The repository gains a Node toolchain, npm workspaces at the root, and the CI `web` job -
already written and never yet triggered - starts running. Every dependency is permissively
licensed, and the same rule applies here as everywhere else in this project.

Decision 1 makes the surface's tests cheaper than they look. There is no rule to test here that
is not tested on the server; what is worth testing is that the surface reflects the platform
faithfully, that it cannot emit content the write gate will reject, and that it never asks a
person for an identifier.

Decision 6 leaves unsaved work in one browser until a draft workspace exists.

Nothing here addresses concurrent editing of the same label by two people, which CAP-LCM-008
asks for and which is a lifecycle question rather than a surface one.
