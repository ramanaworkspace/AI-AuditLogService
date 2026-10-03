# Canonical event hashing

## Purpose

The hash chain makes changes to stored event content detectable when records and
their chain relationships are verified. Tampering with a chained record can be
detected when the affected record and subsequent chain relationships are
verified. Hashing alone does not prevent changes to storage, prove who made a
change, or protect against an attacker able to replace the entire chain and its
trusted checkpoints.

## Fields covered

The canonical record contains these immutable event fields, in the exact order
shown:

1. `eventId`
2. `sequenceNumber`
3. `eventType`
4. `actorId`
5. `resourceType`
6. `resourceId`
7. `payload`
8. `timestamp`
9. `previousHash`

`contentHash` is not included in the canonical record: including a value in its
own hash input would be circular. `EventId` is a lowercase, hyphenated GUID.
`SequenceNumber` is a positive integer assigned by the append process; clients
must not control chain order. `Timestamp` is assigned by the server in UTC.

## Canonical JSON rules

- The top-level property names and their order are fixed as listed above.
  Property names are case-sensitive lower camel case.
- Payload objects are serialized recursively with property names sorted using
  .NET ordinal string ordering (UTF-16 code units). Duplicate property names are rejected
  because their meaning is ambiguous. Array order is preserved.
- Strings use explicit canonical escaping: quotes, backslashes, and control
  characters are escaped; standard short escapes are used for backspace, form
  feed, newline, carriage return, and tab; all other non-ASCII UTF-16 code units
  are emitted as lowercase `\uXXXX` escapes. The resulting canonical JSON text
  is encoded as UTF-8. Unicode string values round trip without loss.
- `timestamp` is emitted in UTC as
  `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`, with exactly seven fractional digits.
  The PostgreSQL append path truncates the server-assigned value to microsecond
  precision before hashing; the seventh digit is therefore zero. This preserves
  the hashed value across persistence without changing the canonical format.
- JSON `null` values inside a payload are preserved; properties are not omitted.
  The payload itself must be a JSON object.
- Booleans and null use the lowercase JSON literals `true`, `false`, and `null`.
- Every JSON number is represented exactly as normalized scientific notation:
  one nonzero leading digit, an optional fractional significand containing no
  trailing zero, lowercase `e`, and a base-10 exponent without a plus sign or
  leading zeroes (for example, `100`, `1e2`, and `1.00e+2` all become `1e2`).
  Positive and negative zero both become `0`. This avoids dependence on the
  source number spelling or floating-point rounding.
- No insignificant whitespace is written.

The serializer produces the UTF-8 bytes of this exact JSON representation.
Hashes are lowercase hexadecimal SHA-256 digests of those bytes.

## Chain construction

The deterministic genesis value is the literal string `GENESIS`; it is not
randomly generated and is not itself a SHA-256 digest.

- Record 1: `previousHash = GENESIS`; `contentHash` is SHA-256 of the canonical
  record containing that `previousHash`.
- Each subsequent record stores the immediately preceding record's
  `contentHash` in `previousHash`; its `contentHash` is then SHA-256 of its own
  canonical record.

`contentHash` is validated as exactly 64 lowercase hexadecimal characters.
`previousHash` must be either the exact genesis literal or a value of that same
SHA-256 format. Verification recomputes the content hash and checks that each
record's `previousHash` equals the preceding record's `contentHash`.

## Limitations

The chain is tamper-evident, not tamper-proof. Recomputing later hashes after
editing a record can produce a self-consistent replacement chain unless a
trusted checkpoint, signature, or separately protected copy exists. Hash
verification does not provide confidentiality, authenticity, non-repudiation,
or authorization. SHA-256 collision resistance is assumed. The service still
needs appropriately protected storage, controlled access, reliable backups,
and independent verification procedures.