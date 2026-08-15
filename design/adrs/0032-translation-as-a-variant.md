# ADR-032: Translation as a variant, linked to a source version

Status: accepted
Date: 2026-08-15

Realises CAP-LOC-001 and prepares CAP-LOC-003 and CAP-LOC-005. Required by iteration 3
([iteration-3.md](../iteration-3.md) acceptance criterion 6, delivery row 8).

## Context

A marketing authorisation in the European Union covers many markets in many languages. The
English source is written once; the Bulgarian, Finnish and Portuguese leaflets say the same
things in different words, are reviewed by different people, and are approved on their own
timetables.

The modelling question is what a translation *is* relative to its source, and the three answers
are genuinely different rather than a matter of taste.

A translation cannot be a **version** of the source. Versions are a lineage in time - version 3
supersedes version 2 - and a French leaflet does not supersede the English one. Making them
versions would put unrelated content in one lineage and make "the latest version" meaningless.

It could be a **dimension alongside version**: a matrix of version by language. Conceptually
tidy, and it changes the shape of identity, of every store, and of every query that has so far
been about one document at one version.

Or it could be **content of its own, linked to what it was translated from**. Which is what the
platform already does twice: a template records what instantiated a label (ADR-021), and a
section records the unit it borrowed and the version it borrowed at (ADR-026).

## Decision

**1. A variant is content with its own identity, its own version lineage and its own lifecycle.**
It is authored, reviewed, approved and signed for like any other label, because in every market
that receives it, it is one. Nothing about it is a special case of the machinery already built.

**2. It records the source document and the source version it was translated from.** Pinned to a
version, for the reason every reference here is pinned: a link to "the English label" without a
version points at whatever that label says today, and a translation is a translation of
something specific.

**3. A variant declares its language, and where they apply its country and regulator**
(CAP-LOC-001). Language is what makes it a translation; country and regulator are what make it a
market variant, and a market variant that is not a translation - the same language, a different
regulator's requirements - is the same shape and is not a special case either.

**4. Section identity is carried over from the source, unchanged** (ADR-015 decision 6). This is
what makes section-level comparison possible at all, and it is why a cross-reference copied into
a translation resolves there (ADR-028 consequences). A translation that minted new section
identifiers would be a document nothing could be said about relative to its source.

**5. Staleness is derived, never stored, and never alters the translation.** A variant linked to
source version 3 is out of date exactly when the source has a version 4 - a comparison, made
when asked. Writing a "stale" flag onto the variant would modify approved content to record a
fact about a different document, and it would be wrong from the moment the next source version
landed until something noticed.

**6. A variant is not automatically anything.** A new source version does not create, update or
invalidate a translation. It makes existing translations *comparable and out of date*, which is
information, and what to do about it is a decision somebody takes through the ordinary write and
approval path.

## Alternatives considered

- **A translation as a version of the source.** Rejected in the Context: versions are a lineage
  in time, and a French leaflet does not supersede an English one.

- **Language as a dimension alongside version.** The complete answer, and the invasive one:
  identity, every store, and every query so far written about one document at one version would
  gain a dimension. It also assumes every variant differs only by language, which market variants
  do not - a different regulator can require different content in the same language.

- **A "stale" flag maintained when a source version lands.** Fast to read, and it writes to
  approved content to record something about a different document. Rejected under decision 5, and
  for the reason ADR-029 gives about in-force: derived facts that are stored go wrong silently.

- **Translations inside the source bundle, as parallel narrative.** FHIR has translation
  extensions, and for a short string they are reasonable. For a whole leaflet reviewed and
  approved separately per market it would put content with different lifecycles in one immutable
  version, which is the conflation this platform has separated everywhere else.

## Consequences

- A variant flows through every gate a label does, and appears in search as content in its own
  right. Whether search should distinguish variants from sources is a projection question,
  recorded rather than decided.
- "Which translations does this label have" is answered by finding variants that link to it,
  which needs the same reverse index the unit references need. Same debt, same home.
- Linguistic review - routed, signed, and segregated from the translator (CAP-LOC-007) - needs
  nothing new: it is the existing lifecycle engine, the existing signature gate and the existing
  routing, configured for a variant's own state model.
- Section-level translation status (CAP-LOC-005) becomes possible because section identity is
  shared, and is not built here.
