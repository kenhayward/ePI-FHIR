# ADR-033: Deterministic rendering, and the two PDF lineages

Status: accepted
Date: 2026-08-15

Realises CAP-RND-001, CAP-RND-003 and CAP-RND-007, and establishes the invariant behind
CAP-RND-002. Extends ADR-010, which chose the toolchain without settling what a render *is*.
Required by iteration 3 ([iteration-3.md](../iteration-3.md) acceptance criteria 8 and 9,
delivery rows 10 and 11).

## Context

A FHIR Bundle is not a leaflet. Everything built so far is correct and unreadable to the person
the label exists for, and a render is where the platform stops being a content store and starts
being something a regulatory affairs professional recognises.

Two things have to be settled before any output is produced.

**What makes two renders the same.** If the same approved version rendered twice can differ,
then a render is a convenience and never evidence: nobody can say the PDF filed with a regulator
is the one this content produces. Determinism is not a performance property here, it is what
makes the output attributable.

**Which PDF is which.** D1 Section 3.3 and D3 Section 3.2 already say that *rendered* PDF and
*artwork* PDF are distinct lineages that are never interchanged - the first produced by this
platform from FHIR, the second produced externally by agencies and only ingested and linked.
That invariant exists in the specifications and nowhere in the code, which is how invariants are
lost.

## Decision

**1. A render is a pure function of a label version and a render-template version.** Nothing
else may reach it: not the clock, not the environment, not a counter, not the identity of who
asked. Given the same two inputs, the bytes are the same, today and in five years.

**2. Render templates are content** (the programme's answer, and ADR-021's reasoning applied
again). Versioned, immutable per version, approved by a regulatory owner. A render template
determines what a patient reads, so somebody signs for it.

**3. A render records both versions it came from.** The label version and the template version,
on the output, so "which template produced this" is answerable from the artefact rather than
from a log. Exactly the shape of ADR-021's template provenance and ADR-026's unit references.

**4. Nothing time-varying is embedded.** No generation timestamp, no build number, no
environment name. This is the decision that makes decision 1 achievable rather than aspirational,
and it is the one that will be argued with, because a generated document with no generation date
feels wrong. The date that belongs on a leaflet is the date of the *content* - the version's
approval and effective dates - and those are facts about the label, not about the run.

**5. A render is derived and belongs in the asset store, keyed by the two versions.** Never in
the FHIR core, which holds canonical structured content only. Losing every render loses nothing
that cannot be produced again, which is the test of whether something is derived.

**6. Rendered and artwork are separate types, and neither can be stored as the other.** Not a
flag, not a folder convention, not a naming rule: a type, so that storing artwork as a render is
a thing that does not compile rather than a thing that gets reviewed. The two have different
provenance, different lifecycles, and different meanings to a regulator.

**7. A render of an unapproved version is marked as a draft** (CAP-RND-004). An author preview
that is indistinguishable from an official render is a document that will eventually be sent to
somebody.

## Alternatives considered

- **Embed a generation timestamp, and compare renders ignoring it.** The usual answer, and it
  makes byte-identity a property of a comparison function rather than of the output. Every
  consumer then needs the same exclusion rule, and the first one that does not have it reports a
  difference that is not one.

- **Render on demand and never store.** Attractive: nothing to keep in step. Rejected because a
  regulator asks for the artefact that was submitted, and reproducing it depends on the toolchain
  still behaving identically - which decision 1 aims at but cannot guarantee across a renderer
  upgrade. Storing the bytes makes the artefact the evidence and the function a convenience,
  which is the right way round.

- **One PDF type with a `kind` field.** Smaller model. Rejected under decision 6: a field is
  checked by whoever remembers, and the failure - artwork served as a rendered label, or a render
  filed as artwork - is exactly the kind a regulated organisation cannot discover cheaply.

- **Render templates as configuration under `config/`.** The other answer to the ownership
  question, and the programme's answer was regulatory. An administrator editing a file is the
  wrong control for something that determines what a patient reads.

## What the print engine actually does, measured

Decision 1 is a claim about bytes, so it was worth testing against the print engine rather than
asserting. Rendering the same HTML twice through Gotenberg (Chromium) on the development stack:

- **12,940 bytes of output, of which exactly 2 differ** - the seconds digits of `/CreationDate`
  and `/ModDate` in the PDF's info dictionary. Content streams, fonts and object structure are
  byte-identical.
- **Gotenberg's `metadata` form field does not override them.** It writes metadata elsewhere in
  the file (the output grew by about 3,300 bytes) and Chromium's own dates remain.

So PDF determinism is achievable and needs one step: **normalising `/CreationDate` and
`/ModDate` after the engine returns**, set from the content's own date. That is not a compromise
bolted on to rescue the decision - decision 4 already says the date that belongs on the artefact
is the date of the content, and this is that decision applied to the one field Chromium insists
on writing for itself.

Recorded here because it is cheap to measure once and expensive to rediscover, and because the
alternative anybody reaches for first - comparing PDFs while ignoring their dates - is the
approach the alternatives section rejects.

## Consequences

- Rendering is a pure function over content plus a template, so it is testable without a browser,
  a container or a filesystem. The PDF step (ADR-010's print engine) is the part that needs a
  container, and it consumes the HTML this produces.
- A renderer upgrade may change output for the same inputs. That is a change to the render
  template's *toolchain* and belongs in the same record as the template version; recorded as a
  debt rather than solved here, and it is the reason decision 5 stores the bytes.
- Accessibility (CAP-RND-005) is a property of the template rather than of the engine, which is
  the right place for it: it is reviewed and approved with the template.
- Scheme transformation to SPL (CAP-RND-006) is a different output from the same source and fits
  decision 1 unchanged - a different template, the same function.
