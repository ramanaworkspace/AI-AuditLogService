# Scenario B: structured payload redaction

## Requirement and scope

Configured sensitive payload values must be replaced at ingest without changing
an already chained event. New events retain an immutable commitment-bearing
payload, not the original sensitive plaintext. Normal reads and the export
projection return redacted values while preserving non-sensitive fields.

This implements structured redaction only. It adds no retention API, physical
deletion, compliance reporting, load harness, or bulk export endpoint. The
`IAuditEventExportProjector` service provides the safe event representation for
export consumers; scoped bundles, chain-boundary context, manifests, and offline
bundle verification remain separate work.

## Configuration

The API's [settings](../../src/AuditLogService.Api/appsettings.json) contain:

```json
{
  "Redaction": {
    "SensitivePaths": ["/accountNumber", "/personalId"]
  }
}
```

Paths are exact, case-sensitive, non-root JSON pointers, not wildcard JSONPath:

- `/accountNumber`: a top-level value.
- `/person/id`: a nested value.
- `/items/0/id`: a value inside the first array item.
- `/a~1b/~0id`: keys `a/b` and `~id`; `~1` means slash, `~0` means tilde.
- `/`: an empty-name property, not the root.

Selected values may be strings, numbers, booleans, null, objects, or arrays.
Selecting an object/array protects its entire subtree. Missing paths do nothing.
Array positions use zero-based decimal indexes without leading zeroes.
There are no wildcards, recursive selection, automatic sensitive-field detection,
or mandatory-presence checks. Configure every relevant occurrence explicitly.

The policy is validated at startup. Invalid escaping, missing leading slash,
duplicate/ancestor-overlapping paths, reserved marker tokens, scalar settings,
non-numeric array keys, and nested/non-string entries are rejected. An absent
policy yields no configured selections; explicitly configuring no selections is
not a privacy guarantee. IConfiguration providers merge array entries, so a
shorter override can retain lower-priority entries: review the complete effective
configuration. Environment keys take the form `Redaction__SensitivePaths__0`.
Policy changes require a process restart.

## Commitment construction

For each matched value:

1. Canonicalize the original JSON value using the existing canonical payload
   rules: ordinal-sorted keys, preserved array order, normalized exact numbers,
   explicit Unicode escaping, UTF-8 bytes, and preserved nulls.
2. Generate a fresh 32-byte salt with `RandomNumberGenerator`.
3. Calculate:

```text
digest = SHA256(
    UTF8("AuditLogService.PayloadCommitment.v1\0")
    || saltBytes
    || canonicalValueBytes
)
```

The prefix ends with one zero byte; `||` denotes byte concatenation. The salt is
the raw 32 bytes in the hash input, not its hexadecimal spelling. Store:

```json
{
  "$auditCommitment": {
    "scheme": "sha256-salted-v1",
    "salt": "<64 lowercase hexadecimal characters>",
    "digest": "<64 lowercase hexadecimal characters>"
  }
}
```

The salt is **public**, persisted with the digest, and independently randomized
for each selected field and append. Identical values ordinarily produce different
commitments. In contrast, serializing/hashing the same already committed event
remains deterministic. No encryption key or secret commitment key is used.

Client input containing `$auditCommitment` anywhere is rejected, preventing
client-supplied commitment envelopes. Duplicate object names anywhere, including
inside selected subtrees, are rejected before replacement. Errors do not include
input values or offending property names.

## Storage, API, and chain integrity

The append service protects the payload before creating a database context or
transaction. The existing global advisory-lock transaction inserts the immutable
event, inserts a separate `audit_event_read_projections` row, and advances the
chain head atomically. Failure leaves no partial event/projection/head update.
The migration adds only this JSONB projection table with EventId primary key and
a restrictive foreign key to the immutable event.

The event hash includes the **commitment-bearing immutable payload**, including
the scheme, salt, and digest. Redacted projection data is outside the hash input.
Existing EventId, sequence, timestamps, and chain rules are unchanged.

- POST `/api/v1/audit-events` returns selected values as `"[REDACTED]"`.
- GET `/api/v1/audit-events` returns the safe projection. Non-sensitive values
  remain readable; normal responses do not expose commitment envelopes.
- GET `/api/v1/audit-events/verify` reads immutable data, including archived
  records. Editing a commitment produces a content-hash violation.
- Export projection applies the same masking, never restoring a value.

Query projection is reapplied under current policy and compared with a projection
derived from the immutable event; an inconsistent saved projection is rejected
with a generic server error. Commitment-marker masking is independent of current
path configuration, so removing a path does not reveal an existing commitment.
Projection corruption does not itself invalidate the event chain.

A redacted API/export payload cannot be used to recompute the event's ContentHash:
verification needs the immutable commitment-bearing representation. This safe
export projection is **not** an independently verifiable export bundle.

## Errors and logging

Protection rejects ambiguous/reserved payloads with a fixed 400 validation error.
Malformed binding returns 400 without echoing input. Unexpected errors use generic
Problem Details, including in Development, rather than response stack traces.
Rejection logs are fixed messages, not payloads. Sensitive new payload values are
removed before EF/Npgsql can receive them. The implementation does not log request
bodies or enable EF sensitive-data logging.

The original request and parsed value exist transiently in managed memory; a
canonical byte buffer is cleared after commitment creation, but this is not secure
erasure of all request buffers, strings, crash dumps, or process memory.

## Rollout and threat model

This prevents storing newly ingested configured sensitive plaintext in the event
or projection and prevents its disclosure through normal reads or the export
projector. It does not make tampering impossible. Tampering with a chained record
can be detected when the affected record and subsequent chain relationships are
verified.

Important limitations:

- Public salts mitigate equality correlation and precomputed tables, **not
  per-record dictionary attacks**. Low-entropy identifiers remain guessable by
  anyone who obtains the stored commitment. This is not encryption.
- A candidate original value plus the stored salt can be checked against a
  commitment. The service retains no original and offers no restoration API.
- Protection is limited to configured payload values, not property names,
  event/actor/resource identifiers, or copies at unconfigured paths.
- Existing already-chained plaintext cannot be retroactively erased without
  changing hash evidence. Legacy rows without projections receive a safe derived
  read/export projection under current policy; their original database content
  and backups remain unchanged. This is **not retrospective plaintext removal**.
- Application tests do not prove privacy of reverse proxies, external telemetry,
  database administration tools, backups, or crash dumps. Do not enable request
  body or sensitive-parameter logging. Control these independently.
- Privileged attackers can alter the database or replace a whole self-consistent
  chain; independently protected checkpoints remain necessary.

For production, govern and validate policies before rollout, restrict database
and export access, review entropy of selected values, and assess keyed
commitments/HMAC where offline guessing is unacceptable. A keyed scheme requires
versioned envelope semantics, secure key storage, rotation/retirement rules,
availability and recovery planning, and careful migration without rewriting
existing chained records. There is no implicit encryption/key-management feature
in this prototype and no claim of regulatory compliance.

## Test evidence

Redaction coverage consists of 19 unit and 15 PostgreSQL/API integration cases:
top-level/nested/array/escaped paths; multiple and null/subtree values;
non-sensitive preservation; independently recomputed commitment digests; fresh
salts; invalid configuration and reserved/duplicate input; redacted POST, query,
and export; valid chains; commitment tampering; restart/policy removal; legacy
read masking without hash rewrites; altered projection rejection; and safe
validation/database-failure responses and captured logs. Database-failure tests
also verify transactional rollback.

Run against the dedicated PostgreSQL test database:

```powershell
dotnet test AuditLogService.sln --filter FullyQualifiedName~Redaction
dotnet build AuditLogService.sln
dotnet test AuditLogService.sln --no-build
```

Validation outcome: the solution build succeeded and all 127 tests passed
(54 unit and 73 integration), with no failures or skips. The EF pending-model
check reported no model changes since the last migration. The existing EF CLI
8.0.0 emitted a version warning against EF runtime 10.0.0; use the Infrastructure
project as both migration and startup project for its design-time factory.
The migration was applied by the fixture to the dedicated test database, not to
the development database. These tests make bounded non-disclosure assertions,
not a general confidentiality proof.
