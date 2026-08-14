# ADR-023: Reconstructing a historical version

Status: accepted
Date: 2026-08-14

Realises CAP-LCM-006 and CAP-LCM-011, and the version half of CAP-SCH-002. Required by
iteration 2 ([iteration-2.md](../iteration-2.md) acceptance criterion 8).

## Context

CAP-LCM-006 asks the platform to "reconstruct the full content and metadata of any historical
version". Three quarters of that already exists and was built in the first two increments:
content is immutable per version with a version lineage (ADR-015), transitions are append-only
so state at any past moment is derivable rather than overwritten (ADR-019), and an approval
carries a signature over the hash of the exact bytes signed (ADR-020).

What is missing is the part that is not stored anywhere: **what the version was approved
against**. The platform validates against a pinned implementation guide (ADR-016), mints
identifiers into a configured authority (ADR-017), applies a state model held as configuration
(ADR-019), and scaffolds from a template with its own version (ADR-021). Every one of those is
configuration, and configuration moves. In three years the pinned IG will be a later release,
the state model will have gained a state, and the answer to "what would we validate this
against" will be a true answer to a different question from "what was this validated against".

An inspection does not ask what the platform would do today. It asks what was done then, and
expects the record to have been made then.

There is a second, subtler failure to avoid: answering the question by **re-validating**. It is
tempting, and it produces a confident green tick. A verdict computed today with today's
packages is evidence about today. If the packages have moved it is not even evidence that the
content was ever valid, and if they have not moved it adds nothing the record did not already
say.

## Decision

**1. The validating context is recorded at approval, not looked up on demand.** Approval is the
moment the organisation commits to a version, and it is the moment CAP-LCM-011 already names
for pinning the content snapshot. The pin is made from what was in force at that moment, and
never afterwards.

**2. What is pinned:**

- The **conformance packages** the content was validated against - name, version and SHA-256
  digest of each, as vendored (ADR-016). The digest is what makes the pin checkable rather than
  merely descriptive.
- The **content hash** of the approved version, computed the same way a signature's is
  (ADR-020), which ties the pin to exact bytes rather than to a version number.
- The **state model** by name, and the state the version reached.
- The **template and template version**, where the content came from one. Already carried on
  the content itself (ADR-021 decision 4), and restated here so the record stands alone.
- The **identifier authority**, because the identifiers in the record mean nothing without the
  namespaces they belong to (ADR-017).

**3. The pin lives in the lifecycle store, append-only, one per approved version.** Not on the
content, which is immutable and whose bytes the pin is *about*; and not in configuration, which
is the thing it exists to outlive. A second pin for a version already pinned is refused rather
than overwriting the first: a record that can be replaced is not a record.

**4. Reconstruction is a read, never a re-computation.** The platform returns the content, the
pinned context, the full transition history, the per-market history and the signatures. It does
not re-run validation and it does not assert that the version "is valid" - it reports what was
recorded, and whoever is asking may draw the conclusion. This is the difference between
evidence and reassurance.

**5. The digests are checked but reported, not enforced.** A reconstruction states whether the
packages present now match the digests recorded then. Where they do, the pinned context is
reproducible from the repository; where they do not, that is a material finding and the answer
says so rather than failing the request. Refusing to answer would deny an inspection exactly the
information it came for.

**6. A specific version is retrievable over HTTP.** Reconstruction is worthless while the only
reachable content is the latest version. This is also the outstanding half of CAP-SCH-002.

**7. An unapproved version has no pinned context, and says so.** A draft is reconstructable as
content plus history, which is all it ever was; inventing a context for it would put a record
of commitment against something nobody committed to.

## Alternatives considered

- **Re-validate on request and report the verdict.** Rejected in the Context above: it answers
  a different question, and the confident green tick is worse than no answer because it will be
  read as the answer to the question that was asked.

- **Record the context on the content itself, as tags or an extension.** Attractive because it
  travels with the bytes. Rejected: it would change the content the hash is computed over
  depending on when the tag was applied, and the pin describes an act of the organisation
  rather than a property of the label. It would also put mutable-looking metadata on a resource
  whose whole value is being immutable.

- **Pin on every write rather than at approval.** More uniform, and it records a commitment
  against drafts nobody relied on. Rejected: CAP-LCM-011 names approval, and a pin against
  every draft revision makes the pins that matter harder to find rather than easier.

- **Reconstruct the configuration from version control instead.** The repository does hold the
  history of `config/` and `profiles/`, so in principle the context is recoverable by dating a
  commit. Rejected as the primary mechanism: it ties an evidentiary record to a development
  tool, assumes the deployment ran exactly what was on a branch at a moment nobody wrote down,
  and cannot survive the repository being reorganised. It remains a useful corroboration.

- **Store the packages themselves alongside each approval.** The most complete answer, and it
  removes the digest-mismatch case entirely. Rejected on proportion: the pinned packages are
  around eight megabytes, they are vendored in the repository already, and copying them per
  approval trades a large, growing store for a case the digest already detects.

## Consequences

- Pinning happens where content and lifecycle already meet, which today is the transition
  endpoint rather than inside the lifecycle engine: the engine knows nothing about content
  bytes or conformance packages. That leaves the same non-transactional seam already recorded
  for lifecycle registration, and it is recorded again here rather than assumed away.
- The pin's fidelity depends on the packages actually in use at approval matching the manifest.
  `tools/verify-profile-packages.py` already enforces that in CI, which is what makes reading
  the manifest an honest substitute for hashing what the validator loaded.
- Terminology versions are not pinned, because terminology is not yet bound (capability 6).
  When Snowstorm arrives its content version belongs in this record, and the R4/R5 question
  recorded against ADR-016 has to be settled first.
- Effective dating is not part of the pin. It is a property of the approved version rather than
  of the context it was approved against, and it arrives with supersession.
