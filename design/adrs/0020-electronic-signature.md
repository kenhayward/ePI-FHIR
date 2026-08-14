# ADR-020: Electronic signature

Status: accepted
Date: 2026-08-14

Realises CAP-AUD-003. Required by iteration 2
([iteration-2.md](../iteration-2.md) Section 7 decision 2, acceptance criterion 3).
Builds on [ADR-018](0018-audit-event-contract.md) (audit contract) and
[ADR-019](0019-lifecycle-state-model.md) (lifecycle state model).

## Context

ADR-019 built a transition engine that refuses an approval unless a signature reference is
supplied, and deliberately left the reference an opaque string: the model knows a gate must be
signed, and knows nothing about how signing works. This ADR fills that hole.

The regulatory frame is 21 CFR Part 11 Subpart C and EU Annex 11 clause 14. Part 11 admits
non-biometric electronic signatures built from **at least two distinct identification
components** (Section 11.200(a)(1)), requires a signature manifest carrying printed name, date
and time, and **meaning** (Section 11.50), and requires the signature to be **linked to its
record** so it cannot be excised, copied, or otherwise transferred (Section 11.70).

Two things make this decision worth writing down rather than assuming. First, the platform's
target adopter is not yet known, and the credential mechanism a pharmaceutical company already
operates is the one it will want to use. Second, and more importantly, the boundary between what
a signature mechanism achieves and what an organisation's procedures achieve is exactly where a
regulated buyer will press hardest, and it is easy to overclaim by accident.

## Decision

**1. Two identification components, re-entered at the point of signing.** The signer supplies a
user identifier and a password at the signing gate itself. An existing authenticated session is
not a signature: a signature any open session can produce records that a browser was logged in,
not that a person assented. Part 11 Section 11.200(a)(1)(ii) permits using all components only at
the first signing of a continuous session and one component thereafter; **we deliberately do not
take that concession**, because approvals are infrequent and the stronger behaviour is the easier
one to explain.

**2. The credential check is a port, not a mechanism.** `ICredentialVerifier` takes an identifier
and a password and returns the verified signer identity or nothing. The demonstration implements
it against Keycloak, which already holds users and passwords; production is expected to implement
it against PKI. The manifest, the hash, the storage, and the gate do not change when the
credential mechanism changes - only that one implementation does. This is why it is a port.

**3. The printed name comes from the identity provider, not the caller.** The verifier returns
the signer's identifier and printed name together, and the service records what the verifier
returned. A caller that could state the signer's name could sign in someone else's name, which
is the same reasoning that makes `LifecycleService` read a version's author from the store rather
than accept it as an argument.

**4. What is signed is a SHA-256 hash of the canonical serialisation of the version.** Not the
content itself: the content is already immutable and retrievable by version, so storing it again
in the signature would duplicate it without adding evidence. The hash is what makes later
alteration detectable, which is what Section 11.70 asks of the link.

**5. Server-assigned metadata is excluded from the hash; platform identity and version are not.**
The hash covers the bundle with its logical id, `meta.versionId`, and `meta.lastUpdated` removed,
because those are assigned by whichever FHIR server holds the content and differ after a restore,
a re-index, or a migration between servers. A hash that changed under those operations would make
every historical signature unverifiable for reasons that have nothing to do with the content. The
platform's own identifier and version tag stay in, so the hash covers *which* version was signed
and not merely what it said.

**6. The manifest records signer, printed name, time, meaning, and what was signed.** Meaning is
drawn from a closed set - authorship, review, approval - rather than free text, because
Section 11.50(a)(3) requires the meaning to be recorded and a free-text field records whatever
the caller felt like typing. The time is taken from the platform's clock at the point of signing,
not supplied by the caller, for the reason ADR-018 already gives for audit records.

**7. The signature is persisted append-only and linked to the version.** It goes to the same
append-only regime as the audit trail from iteration 1, which refuses `UPDATE` and `DELETE` at
the database rather than by convention. The link to the record is the document identity, the
version, and the content hash together: a manifest copied onto another version is detectable
because its hash will not match that version's content.

**8. The password never enters a record, a log, or an exception.** It is a parameter to the
verifier and nothing else. No derived form of it is stored either: a password hash in a signature
manifest would be a credential in an append-only store that by design cannot be purged.

**9. A refused signing attempt is recorded as deliberately as a successful one.** Section 11.300(d)
requires that attempted unauthorised use be detected and reported, and a wrong password at an
approval gate is precisely that signal. Recording follows ADR-018: a decorator, so no path can
sign without the attempt being recorded.

**10. This is not a cryptographic signature, and the documentation says so.** A password-based
electronic signature attests that someone who knew a credential asserted a meaning at a time. Its
integrity rests on the append-only store and on access control, not on a key that only the signer
holds. Part 11 permits this because the surrounding controls are procedural and system-level
rather than cryptographic. Saying otherwise in a sales conversation would be the kind of claim
that is discovered in an audit rather than in a demonstration.

## What is met by mechanism, and what is met by process

Naming this boundary honestly matters more than the feature. The platform provides the left
column; an adopting organisation must provide the right one, and no amount of code substitutes
for it.

| Part 11 requirement | Met by |
|---|---|
| 11.50 signature manifest: name, date, time, meaning | **Mechanism** - the manifest |
| 11.70 signature linked to its record | **Mechanism** - document identity, version, and content hash |
| 11.200(a)(1)(i) two identification components | **Mechanism** - identifier and password at the gate |
| 11.200(a)(1)(iii) used only by their genuine owner | **Both** - the platform checks the credential; the organisation must forbid sharing it |
| 11.10(e) append-only audit trail | **Mechanism** - the database refuses update and delete |
| 11.300(d) detection of attempted unauthorised use | **Mechanism** - refused attempts recorded |
| 11.100(b) identity of the individual verified before credentials are issued | **Process** - identity proofing, outside the platform |
| 11.100(c) certification to the agency that signatures are legally binding | **Process** - an organisational undertaking, not a feature |
| 11.300(a) uniqueness of the identifier and password combination | **Process and configuration** - the identity provider's realm policy |
| 11.300(b) password ageing, complexity, and revocation | **Process and configuration** - Keycloak policy, not our code |
| 11.300(c) loss management for tokens and cards | **Process** - not applicable to passwords; relevant under PKI |
| 11.10(i) training and standard operating procedures | **Process** |

## Alternatives considered

- **Treat the session token as the signature.** Cheapest, and common in systems that claim Part 11
  compliance. Rejected: it records that a session existed, not that a person assented, and it
  fails the intent of Section 11.200 even where it might survive a reading of its letter.
- **Store the signed content rather than a hash of it.** Rejected: the content is already
  immutable and retrievable, so this duplicates it in an append-only store that cannot later be
  pruned, and adds no evidence the hash does not already provide.
- **Sign with a server-held key (HMAC or a platform private key).** Rejected for the
  demonstration, and worth being clear about why: it would prove the *platform* attested, not that
  the *person* did, unless the key is the person's own. Dressing a server key up as a signature
  would look more rigorous while proving less about the signer. Signer-held keys are what PKI
  provides, and PKI is the expected production mechanism.
- **Free-text signature meaning.** Rejected: Section 11.50(a)(3) requires the meaning to be
  recorded, and a set of three understood values is reportable where free text is not.
- **PKI now.** Rejected for the demonstration only, on lead time rather than on merit: certificate
  issuance and identity proofing are the adopting organisation's, and cannot be stood up against
  an adopter who is not yet known. Decision 2 keeps the cost of the change to one implementation.

## Consequences

- The transition engine's `signatureReference` stops being an opaque string: at the approval gate
  it must resolve to a manifest by the same actor over the same version. That wiring, and the
  single-use rule that stops a signature being replayed against a second transition, follow in the
  next pull request.
- The demonstration needs a Keycloak-backed `ICredentialVerifier`, and Keycloak needs a password
  policy configured for the demonstration to be honest about Section 11.300(b).
- Signatures are stored in memory in the pull request that introduces them; a durable append-only
  signature store follows, on the pattern iteration 1 used for the audit sink.
- Because meaning is a closed set, a market or organisation needing another meaning is a
  configuration change to that set, not a schema change - consistent with ADR-012.
- The Part 11 boundary table above is the honest answer to "are you Part 11 compliant". The
  platform is not compliant on its own and no platform can be; it supplies the mechanism half of a
  compliant system.
