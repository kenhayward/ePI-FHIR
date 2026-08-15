# ADR-028: Cross-references between sections

Status: accepted
Date: 2026-08-15

Realises CAP-SCM-005. Required by iteration 3
([iteration-3.md](../iteration-3.md) acceptance criterion 3, delivery row 4).

## Context

Labels refer to themselves constantly: "see section 4.4", "as described in section 2". Those
references are part of the prose, they are read by a patient or a prescriber, and a regulator
notices when one points at a section that is no longer there.

ADR-026 said cross-references would reuse the unit reference shape - `section.entry` carrying an
identifier - because they are both references. That was right about the *targets* and wrong
about the *source*. A unit reference says "this whole section is borrowed"; a cross-reference is
a phrase inside a sentence, and there may be four of them in one paragraph pointing at four
different places. A reference held on the section cannot say which words it belongs to.

Section identity already exists and is exactly what a target needs: assigned at creation,
opaque, stable across versions and translations (ADR-015 decision 6), and stored as the FHIR
element id.

## Decision

**1. A cross-reference is an anchor in the narrative, targeting a section identity.** Written as
`<a href="#{section id}">`, which is the FHIR-idiomatic internal reference and is permitted in
the constrained XHTML `Narrative.div` allows. It lives where the words live, which is the only
place it can carry which words it belongs to.

This supersedes ADR-026's expectation. Unit borrowing stays on `section.entry` - it is a
statement about the whole section - and cross-referencing does not.

**2. An internal reference resolves within the version that carries it.** Not against the latest
version of the document, and not against the document as a concept: against the immutable bytes
that contain the reference. So a cross-reference cannot rot, because the thing it points into
cannot change.

**3. Referential integrity is checked at the write gate, and a dangling reference is refused.**
A label that points at a section it does not have is a label with a broken instruction in it,
and the write gate is the last place it can be caught cheaply. This is what CAP-SCM-005 means by
integrity.

**4. A cross-document reference names the document, the version and the section.** Pinned, for
the reason every reference here is pinned: an unversioned reference points at whatever that
document says today. Integrity for those is *not* checked at the write gate - the target is
another aggregate, possibly not yet written, possibly out of scope for the caller - so they are
recorded and checked on resolution instead, and the difference is stated rather than blurred.

**5. Nothing rewrites an author's anchors.** The platform validates them and resolves them; it
does not renumber, relabel or repoint them. A system that silently repointed a reference during
an edit would be changing what a label says.

## Alternatives considered

- **`section.entry` references, as ADR-026 anticipated.** Consistent with unit borrowing, and
  unable to say which phrase in a paragraph the reference belongs to. Rejected on that alone.

- **Anchors by section number ("see section 4.4").** What the prose already says, and what every
  legacy system stores. Rejected: section numbers move. Renumbering a label would silently
  repoint every reference in it, and the failure is invisible because the text still reads
  correctly.

- **A FHIR extension on the narrative.** `Narrative.div` is a constrained XHTML fragment and does
  not take extensions in a way that survives round-tripping through the text itself. The anchor
  is already the standard mechanism.

- **Checking cross-document integrity at the write gate too.** Tempting for symmetry. Rejected:
  it makes a write depend on the availability and visibility of another document, so a label
  would fail to save because of something entirely outside it - and the caller cannot be told
  which target failed without disclosing whether it exists.

## Consequences

- Section identity becomes load-bearing for prose, not only for impact analysis. A section
  identifier that changed would break references inside approved content, which is why
  `SectionIdentity.AssignMissing` is idempotent and why that matters more than it looked.
- Authors do not write these anchors by hand; the authoring surface inserts them, and until that
  exists cross-references are written by whatever produces the content. Recorded as a debt.
- The reverse direction - "what points at this section" - needs an index, as with unit
  references. Same debt, same home.
- Translation must carry references across without repointing them: a translated section keeps
  its source's identity (ADR-015 decision 6), so an anchor copied into a translation resolves in
  the translation. That falls out of the design rather than needing work, and there is a test
  for it when localisation arrives.
