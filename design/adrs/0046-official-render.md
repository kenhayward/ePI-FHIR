# ADR-046: Producing and filing the artefact of record

Status: accepted
Date: 2026-08-17

Realises CAP-RND-002 and CAP-RND-004. Discharges [ADR-033](0033-rendering.md) decision 2, which
has been blocked since iteration 3, and is unblocked by [ADR-042](0042-template-store.md),
[ADR-043](0043-durable-template-storage.md) and [ADR-047](0047-signing-for-a-template.md).

## Context

[ADR-033](0033-rendering.md) decision 2 said only an approved render template may produce an
official render. Nothing could satisfy it: there was no template store, so every render was a
preview that said so, nothing was written to the asset store, and the asset store built in
iteration 3 had no caller. Three pieces landed since - a template store, durable storage for it,
and a signature that can reach its approval gate - and this is the thing they were for.

The distinction being kept is CAP-RND-004's, and it is not cosmetic. An author preview
indistinguishable from an official render is a document that will eventually be sent to somebody.

## Decision

**1. An official render needs two approvals: the content's and the template's.** The content
because an official render of a draft is a document somebody will eventually send; the template
because a template determines what a patient reads. Either one missing is a refusal, not a
downgrade - quietly handing back a draft render would produce something that looks like the
artefact of record and is not.

**2. The newest *approved* template version is used, not the newest version.** A template revised
after an approval has a draft at the top, and rendering with that would be rendering with
something nobody signed for.

**3. It is filed in the asset store, keyed by both versions that made it.** Both are inputs to
the bytes (ADR-033 decision 1), so a key naming only the label version would collide the moment a
template was revised. Write-once, under object-lock, with retention from configuration - verified
against the running stack: `X-Amz-Object-Lock-Mode: COMPLIANCE`, retained until 2036.

**4. Asking twice is idempotent, and 200 rather than 409.** A render is a pure function of its two
versions, so a second request asks for the same bytes. Answering with the write-once refusal would
make an idempotent request look like a conflict, and callers would learn to retry through it. The
first request answers 201 and later ones 200 with `alreadyFiled`.

**5. If what is filed differs from what the content and template now produce, that is raised, not
resolved.** Byte-compared on every request. "Reproducible" is a claim about those two things being
equal and this is the only place it can be checked; if they differ, something has changed
underneath a copy somebody already has, and answering with either version silently would hide it.
It is a 500, because it is not a conflict a caller can do anything about.

**6. The filed artefact is served from the asset store, never re-rendered.** What a regulator was
sent is what was filed. Re-rendering to answer a question about the artefact would answer with a
fresh one that ought to match.

**7. Which template to use is the caller's decision, from the list the platform offers.** An
editorial choice, not a platform one - and the platform's job is to say which templates are
approved (`GET /templates`) so that nobody types an identifier from memory (ADR-037 decision 3).

**8. Deciding whether a render may be produced lives outside `Epi.Rendering`.** That project reads
content and nothing else, which is what makes a render a pure function of its inputs. `OfficialRender`
sits in its own module and composes the four things the decision needs: content, lifecycle state,
the template store and the asset store.

## Alternatives considered

**Extend the preview endpoint with an `official=true` flag.** One endpoint, one code path, and it
puts the entire distinction CAP-RND-004 exists for into a query parameter somebody will default
wrongly. Two endpoints that refuse different things are harder to confuse.

**Produce the render at the moment of approval, as a side effect.** Attractive - every approved
version would have its artefact - and rejected: approval would then fail when the asset store was
unavailable, which means an unrelated outage blocks a regulatory act. Approval is a decision;
rendering is a consequence of one, and consequences can be retried.

**Render PDF now, since PDF is what a regulator receives.** In scope for CAP-RND-001 and not for
this decision. The Gotenberg print engine and `PdfRenderer` exist; what is unsettled is whether
PDF bytes are reproducible across engine versions, which is the whole basis of decision 5.
Recorded as the next piece of rendering work rather than assumed.

## Consequences

The asset store has a caller, ten months after it was built. The preview endpoint keeps its
scaffolding template and its "this is a preview" marking, and both are now correct rather than
apologetic: a preview is what you look at while the content is unapproved, and there is a
different thing for when it is not.

An official render is HTML. A regulator receives PDF, so CAP-RND-001 is met in part, and the
missing part is named above rather than glossed.

Nothing here files the render against a market submission, or records which artefact was sent
where. That is publication (capability 14) and it now has something to publish.
