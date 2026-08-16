# ADR-034: Durable asset storage, and where write-once actually comes from

Status: accepted
Date: 2026-08-16

Realises CAP-RND-002. Extends [ADR-033](0033-rendering.md), which settled what a render is and
how it is keyed, and [ADR-013](../D3-technical-architecture.md) (D3 Section 14), which chose
MinIO with object-lock for WORM storage. Required by iteration 3
([iteration-3.md](../iteration-3.md) delivery row 11b).

## Context

ADR-033 gave the asset store a contract and an in-memory reference implementation. The contract
says the store is write-once, and the reference implementation gets that from a dictionary that
refuses a second insert. That is fine for a reference implementation and worth nothing as a
guarantee: application code that refuses an overwrite is application code, and the next caller
- a migration script, an operator with credentials, a service written next year - does not go
through it.

ADR-033 and the contract's own documentation both said the durable implementation would get
write-once "from object-lock rather than from a check in application code". **That statement was
wrong, and this ADR exists partly to correct it.**

## What the object store actually does

Measured against `minio/minio:RELEASE.2025-09-07T16-13-09Z`, the image the development stack
pins, through the S3 API. Recorded because the answer is not what the design assumed:

| Operation | Result |
|---|---|
| `PUT` over an existing key, no conditions | **Accepted.** A read now returns the second object |
| `PUT` over an existing key, under COMPLIANCE retention | **Accepted.** A read now returns the second object |
| `DELETE` of a COMPLIANCE-retained version | **Refused** |
| `PUT` over an existing key with `If-None-Match: *` | **Refused**, 412 Precondition Failed |
| Bucket default retention | Applied to objects written into the bucket, without the writer asking |

The trap is the second row. Object-lock protects a *version*: once written under COMPLIANCE
retention, that version cannot be deleted or altered until its retention expires, and not by
anybody, including the root credential. It does not protect the *key*. A second `PUT` does not
modify the retained version - it creates a new one and makes it current - and every ordinary
read returns the new one. The original survives, undamaged and unreachable by anyone who does
not know to ask for it by version.

For an audit export that is enough: nothing is lost, and an investigator can enumerate versions.
For a render it is not. The whole value of a stored render is that the artefact cited in a
submission is the artefact that comes back when it is asked for by name. A key whose current
object can change is a citation that can silently come to mean something else.

## Decision

**1. Write-once at a key comes from a conditional write; immutability of what was written comes
from object-lock. Both, not either.** They answer different questions and neither answers the
other's:

- `If-None-Match: *` on every `PUT` means the second write of a key is refused by the object
  store, not by us. The refusal happens whether the caller went through our code or not.
- COMPLIANCE retention means the version that was accepted cannot afterwards be deleted or
  altered, including by the credential that wrote it.

Application code no longer decides this. `AssetAlreadyStoredException` is now what the store
*reports* when the object store refuses a 412, rather than what our own dictionary lookup
concludes.

**2. Retention is configuration, not a constant.** How long a render must be kept is a
regulatory question with different answers per market and per artefact class, and ADR-012 says a
question like that is answered under `config/`. The store reads a retention period; it does not
know why that period was chosen.

**3. The lineage is the bucket prefix, and the store never lists without one.** ADR-033 made the
lineage the first key component; here it is also the only way to list. `ListAsync` takes a
lineage because a listing that could span both would eventually be written, and the first time
it returned artwork to something asking for renders, the invariant D1 Section 3.3 states would
be gone with nothing failing.

**4. The store speaks S3, not MinIO.** The client is the S3 API (`AWSSDK.S3`, Apache-2.0)
against a configured endpoint. This is what makes D3's claim that migrating to Azure Blob
immutable is "configuration, not redesign" true rather than aspirational, and it means the
conformance suite runs against whatever the environment provides.

**5. The conformance suite is the contract, and the object store answers it unchanged.** The
same source that the in-memory store satisfies runs against a real MinIO container. Where the
two implementations differ - the in-memory one refuses in a dictionary, the durable one is
refused by the object store - the suite cannot tell, which is the point.

**6. Two behaviours of the object store are asserted directly, not only through the suite.** The
conditional write must be refused, and a retained version must be undeletable. These are the
two facts the design rests on, they are properties of the object store rather than of our code,
and the measurement above shows they are not obvious. If a future MinIO release accepts a
conditional overwrite, the conformance suite would still pass - our code would keep its own
dictionary-free logic and the store would quietly stop being write-once - so they are asserted
where a change in them fails loudly.

## Consequences

The development stack's bucket setup was incomplete and is corrected here: `epi-rendered` and
`epi-artwork` were created with object-lock enabled and no default retention, which enables the
mechanism and then does not use it. An enabled lock with no retention is indistinguishable at a
glance from a protected bucket and offers nothing.

A render that must change is still a new render of a new version, and the refusal is now loud in
one more place. Re-running a render that already exists is a 412 rather than a silent
replacement, so callers that legitimately re-render - a retry after a partial failure - must
read before writing or treat the refusal as success. The store reports the refusal; deciding
what it means is the caller's.

Deletion is not implemented, and not because it was forgotten. There is no operation in this
platform that removes a stored artefact, and a retention period that has expired does not
create one - it only means the object store would no longer refuse.

The reference implementation stays. It is what the unit tests run against, and holding both to
one suite is what makes the durable one substitutable.
