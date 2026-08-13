# ADR-017: Identifier authority as configuration

Status: accepted
Date: 2026-08-13

Completes the open point in [ADR-015](0015-identifier-and-versioning-scheme.md), and realises
part of CAP-CFG-001 and CAP-SCM-007.

## Context

Every identifier the platform mints carries a **system**: a URI naming the authority that
assigns the value. `urn:uuid:0195...` means nothing until you know who minted it and in what
namespace. The same is true of the code systems behind the version, affiliate, and market tags.

ADR-015 recorded the authority as an open point, blocking "the first shared environment". That
framing assumed a known adopting organisation. It is wrong for what this repository actually
is: **a fully worked demonstration of an ePI management approach, built to be shown to several
organisations, none of which is yet the target.** There is no deployment whose go-live the
decision can be attached to.

The value therefore cannot be determined here, and guessing one would be worse than not having
one. A guessed namespace looks authoritative, gets copied into stored content, and is
discovered to be wrong only after records exist that cannot be rewritten.

What *can* be settled now is the mechanism, so that an adopting organisation changes one
configuration file rather than searching the codebase for hard-coded URIs.

## Decision

**1. The identifier authority is configuration, not code.** The systems for document identity,
version, affiliate, and market are held in `config/identifiers.json` and loaded at start-up
like any other configuration. No identifier system appears as a literal in application code.

**2. One authority across every environment.** Development, test, and production of a given
deployment share the same systems. Per-environment identity would mean the same document has a
different identity in each, so content could not be promoted between them, and the audit trail
would not follow it.

**3. The demonstration default uses a domain reserved for documentation.**
`https://epi.example.org/...`, from the range RFC 2606 reserves and which therefore can never
be owned by anyone. It is chosen precisely because it is guaranteed to be wrong in production:
an unset authority is conspicuous rather than plausible.

**4. Configuration validation refuses to load a partial authority.** All four systems must be
present and absolute URIs. A half-configured authority would mint some identifiers into the
adopter's namespace and others into the demonstration's.

**5. Adopting the platform means setting this before storing anything worth keeping.** Not
before development, which is disposable, but before any data intended to survive. That is one
edit to one file.

## How an adopter determines their value

Recorded here because the reasoning is the deliverable, not the value:

1. **Find out whether one already exists.** Organisations doing SPL, IDMP, or RIM work usually
   have a registered OID arc and often an established URI namespace. Extending an existing
   namespace is right; inventing a second one that looks authoritative is worse than having
   none. Ask regulatory affairs and enterprise architecture before designing anything.
2. **Ownership and permanence.** A domain the organisation will control for the lifetime of the
   records, which is decades. Prefer the corporate root domain over a product, programme, or
   brand domain, because those change more often than regulated records do.
3. **Governance.** Someone must own the registry of assigned systems and approve additions
   (capability 21). A named owner, not just a document.
4. **Form.** An HTTPS URI under an owned domain is FHIR-idiomatic and readable. `urn:oid:` is
   equally valid, and preferable only where an arc is already managed.
5. **Regulatory constraint.** Check whether the EU ePI specification prescribes identifier
   systems for published content. If it does, that binds published ePI regardless of internal
   preference.

Note what the system URI is *not*: not an endpoint, not required to resolve, not the service's
base URL, and not per-environment. Using the API base URL is the common mistake, and it breaks
the moment the service is renamed or moved.

## Alternatives considered

- **Choose a plausible-looking namespace now** and change it on adoption. Rejected: it would be
  copied into stored content and audit records, and changing it later means either rewriting
  append-only history, which GxP forbids, or maintaining a translation table forever.
- **Leave the constants in code and edit them per adopter.** Rejected: it makes adoption a code
  change and a release, contradicting ADR-012, and puts identity in the one place a
  configuration reviewer will not look.
- **Derive the authority from the deployment's hostname.** Rejected: identity would then change
  with a migration, a rename, or an environment, which is exactly what decision 2 prevents.
- **Use `urn:uuid:` systems with no authority at all.** Rejected: the values would be unique but
  unattributable, and content exchanged across the option-A boundary could not be traced to who
  minted it.

## Consequences

- Adoption is one configuration file, and this ADR is the checklist for filling it in. For a
  demonstration whose audience is several organisations, that is a better answer than a value.
- The demonstration default is visibly a placeholder in every artefact it appears in, including
  stored content, which is the intent.
- Audit records (capability 19) reference these systems and are permanent, so an adopter must
  set the authority before audit records they intend to keep. Recorded in the configuration
  file itself, where someone changing it will read it.
- Because the tags are `Coding.system` values, the platform is asserting **code systems** as
  well as identifier namespaces. Whether those should become published `CodeSystem` resources
  or governed extensions under `profiles/` (CAP-SCM-006) is a modelling question this ADR does
  not settle; it settles only where the URIs come from.
