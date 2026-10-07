# Architecture: implemented audit log prototype

This document describes the implementation inspected on 2026-10-03, not the
complete intended delivery in [plan.md](../plan.md). See [API](api.md),
[testing](testing.md), [trade-offs](trade-offs.md), and
[final engineering summary](final-engineering-summary.md).

## Problem and requirements

The service records structured audit events in an append-only, globally ordered
history and detects inconsistencies by verifying canonical content hashes and
predecessor relationships. The [assessment](../audit-log-service-assessment-requirements.md)
requires A/B/C and at least one of D/E/F in full, with bounded designs for the
others. The prototype implements A, B retention/redaction, normalized C, and D.
**B bulk verifiable export is not implemented**, so B overall is incomplete.

| Capability | Status |
|---|---|
| Immutable events, canonical hashing, PostgreSQL append/query/verify APIs | IMPLEMENTED |
| Retention eligibility and explicit logical archive service | IMPLEMENTED |
| Configured-path commitment redaction and safe export projection | IMPLEMENTED |
| Scenario B self-contained independently verifiable export | PARTIALLY IMPLEMENTED: projection only; endpoint/bundle absent |
| Normalized CLIENT_ACCOUNT access report, deterministic JSON | IMPLEMENTED |
| 50-writer correctness tests and real HTTP load harness | IMPLEMENTED |
| E background verification/checkpoints/alerts | DESIGN ONLY |
| F authentication/tenant authorization/per-tenant chains | DESIGN ONLY |
| Physical deletion, CSV reports, legal certification, exactly-once requests | OUT OF SCOPE |

## Components

- [Api](../src/AuditLogService.Api/): ASP.NET Core .NET 10 Minimal APIs,
  request binding/validation, Problem Details, health and development OpenAPI.
- [Application](../src/AuditLogService.Application/): append/query/verification
  contracts, pure chain verifier, retention contracts, payload protection,
  export projection, and report policy/serialization.
- [Domain](../src/AuditLogService.Domain/): immutable event/data types,
  validation, `ICanonicalEventSerializer`, `IEventHasher`, and chain rules.
- [Infrastructure](../src/AuditLogService.Infrastructure/): EF Core/Npgsql 10
  context factory, PostgreSQL persistence/migrations, append/query/verification,
  retention, and report implementations.
- [Tests](../tests/): xUnit unit/integration projects and a separate finite
  HTTP load-test console harness.

Dependency direction is API to Application/Infrastructure, Infrastructure to
Application/Domain, and Application to Domain. The domain has no PostgreSQL
dependency. Persistent chain state, not process memory, is authoritative.

## Data model and PostgreSQL

| Table | Contents and responsibility |
|---|---|
| `audit_events` | EventId, SequenceNumber, EventType, ActorId, ResourceType, ResourceId, Payload, Timestamp, PreviousHash, ContentHash |
| `chain_metadata` | Single `chain_id = 1` row; head sequence and hash |
| `audit_event_archives` | EventId key/restrictive event FK and UTC ArchivedAt |
| `audit_event_read_projections` | EventId key/restrictive event FK and redacted JSONB Payload |

EventId is a GUID primary key; SequenceNumber has a unique index and must be
positive. Required strings cannot be blank. Database maximum lengths are 200
for EventType/ResourceType, 300 for ActorId, and 500 for ResourceId. Payload is
a JSONB object; Timestamp is `timestamp with time zone`. Hash constraints require
64 lowercase hexadecimal characters, except the literal genesis predecessor.
Indexes support actor, event type, resource type/id, timestamp, and sequence.
There is no unique predecessor constraint: the append protocol serializes
predecessor allocation. Uniqueness alone would not establish chain correctness.

The initial migration seeds head `(0, GENESIS)` and adds an ordinary
UPDATE/DELETE-blocking event trigger. Archive/projection tables are additive
migrations. Triggers are not protection from a sufficiently privileged database
administrator; least-privilege role provisioning is not delivered.

The API does not run migrations at startup. Apply migrations explicitly to the
intended database. Runtime uses `ConnectionStrings:AuditLogDatabase`; the
design-time factory uses `AUDITLOG_CONNECTION_STRING`. Set both consistently.
The integration fixture applies migrations and resets its dedicated database.

## APIs

Implemented routes are POST/GET `/api/v1/audit-events`, GET
`/api/v1/audit-events/verify`, GET `/api/v1/reports/account-access`, GET `/health`,
and development-only GET `/openapi/v1.json`.

There are no event update/delete, single-event GET, archive-operation, or bulk
export routes. The POST Location header points to an event-shaped URI, but
that URI does not have a GET handler. [API documentation](api.md) defines the
actual contracts, including numeric verification classifications.

## Hash chain, canonicalization, and genesis

The exact hash input has these fixed lower-camel-case properties, in order:

```text
eventId, sequenceNumber, eventType, actorId, resourceType, resourceId,
payload, timestamp, previousHash
```

ContentHash is excluded from its own input. SHA-256 hashes the canonical UTF-8
bytes and returns a lowercase 64-character hexadecimal digest. The deterministic
first predecessor is the literal `GENESIS`, not a random value or digest.
Every later PreviousHash is the immediately preceding ContentHash.

Canonical JSON has no insignificant whitespace. Payload object keys sort
recursively by ordinal UTF-16 ordering; arrays retain order; duplicate keys are
rejected; nulls are preserved. Exact JSON numbers normalize to scientific
notation without floating-point conversion; signed zero becomes `0`.
Strings have explicit escaping, including lowercase Unicode code-unit escapes.
GUIDs are lowercase hyphenated text. UTC timestamps use seven fractional digits.
The append path truncates timestamps to PostgreSQL microseconds before hashing,
so the seventh digit is zero. These are project-specific rules, not a claim of
RFC 8785 conformance. [Canonical hashing](canonical-hashing.md) is normative.

## Transaction model and concurrency

[Append implementation](../src/AuditLogService.Infrastructure/Append/PostgresAuditEventAppendService.cs):

1. Validate/protect configured sensitive payload values before database access.
2. Create a context and begin a PostgreSQL transaction.
3. Acquire `pg_advisory_xact_lock(713204890501)`.
4. Read the persisted global head; assign head sequence + 1 and its predecessor.
5. Assign GUID and authoritative UTC acceptance timestamp; construct/hash event.
6. Insert immutable event and redacted read projection.
7. Update tracked head metadata; save changes.
8. Commit; only then return the event receipt.

Every participating writer uses the same lock. PostgreSQL releases this
transaction-scoped lock on commit/rollback; session-loss cleanup depends on
database failure detection. Context/transaction disposal rolls back uncommitted
work. The event, projection, and head advance are atomic. No in-memory lock,
queue, automatic retry, or distributed coordination service is used.

This gives one global sequence order, not order by producer identity or business
occurrence time. Multiple API processes can coordinate through the same database,
but the tests are not proof of a multi-machine deployment. One serialized write
path intentionally sacrifices horizontal write throughput.

## Verification and failure handling

Standalone verification uses one repeatable-read transaction for event rows,
head, and archive counts. It reads the whole retained chain in sequence order
and stops at the earliest observed inconsistency: duplicate sequence, sequence
gap, genesis/predecessor mismatch, then content mismatch. The persistence layer
also compares the verified tip/count to the head, detecting missing tails or
all-record deletion when head evidence remains.

Missing-record results can provide an expected sequence but no deleted EventId.
Classification describes evidence, not a proven cause. Archived rows are
verified, not skipped. Archive/projection metadata is outside event hashes.

Expected validation failures return 400; malformed binding returns 400.
Ambiguous/reserved payload rejection logs a fixed message without input values.
Unexpected failures propagate to the generic Problem Details handler; the
service does not fabricate successful writes/reports on database failure.
Cancellation reaches database operations.

There is no idempotency key. Connection failure during/after COMMIT can leave
the client uncertain even though a write committed; blind retry may duplicate a
logical event. Tested known-rollback retries are not an exactly-once guarantee.

## Retention

The default configurable window is 90 days, not a legal retention requirement.
Eligibility is strictly `Timestamp < serverUtcNow - Retention.Window`.
`IAuditEventRetentionService.ArchiveEligibleAsync` explicitly inserts separate
archive markers with a parameterized atomic INSERT/SELECT and conflict handling.
Repeated/concurrent operations preserve the first marker.

No scheduler or HTTP archive route invokes it automatically. All original
events remain readable and verifiable. Archival neither reduces storage nor
erases data, hides payloads, or changes authorization. See
[retention design/evidence](scenarios/scenario-b-retention.md).

## Redaction and export

Exact configured JSON pointers select values at ingest. Each selected value is
replaced with a `$auditCommitment` envelope containing scheme, fresh public
32-byte salt, and SHA-256 digest:

```text
SHA256(UTF8("AuditLogService.PayloadCommitment.v1\0")
       || saltBytes || canonicalValueBytes)
```

The immutable event hashes the envelope. Read/report/export projections replace
selected values/envelopes with `"[REDACTED]"`; non-sensitive fields remain.
Saved query projections are checked against a projection derived from immutable
data. Policy removal does not expose existing commitment envelopes.

Public salts do not prevent offline guessing of low-entropy values. Protection
does not cover names/identifiers or copies at unconfigured paths. Existing
chained plaintext/backups are not rewritten or erased; current policy can mask
legacy reads. Transient request memory is not guaranteed securely erased.
See [redaction](scenarios/scenario-b-redaction.md).

`IAuditEventExportProjector` is IMPLEMENTED as a safe projection helper.
There is no bulk export endpoint, chain-boundary package, manifest, or offline
bundle verifier. A redacted projection cannot reproduce the immutable hash.
This is an assessment gap, not a completed verifiable export.

## Compliance reporting

The prototype reports only exact `CLIENT_ACCOUNT` resources and the explicit
SUCCEEDED/DENIED/FAILED account-access event allowlist. Required start/end
instants have offsets, normalize to UTC, and are inclusive acceptance-time
bounds. Items sort by global sequence. Deterministic JSON includes redacted
payloads, archive status, and full global chain/head status from one
repeatable-read snapshot. CSV is excluded from the selected design.

This reports producer assertions, not authenticated identities, completeness
of access capture, legality, certification, or regulatory approval.
See [normalized requirement](scenarios/scenario-c-requirement-clarification.md)
and [Scenario C](scenarios/scenario-c.md).

## Security and limitations

- No authentication, authorization, tenant isolation, or rate limiter is
  configured. Keep this prototype network-isolated.
- Shared local configuration contains credential-bearing defaults; do not
  deploy/reuse them. Use external secrets and separate least-privilege runtime
  and migration identities. No secret values are reproduced in these documents.
- Ordinary queries have bounded keyset pages; reports/full verification
  materialize unbounded histories and can exhaust capacity under repeated use.
- `/health` has no registered database check; HTTP health does not prove
  PostgreSQL reachability, valid chain state, or migration readiness.
- The chain detects inconsistencies when verified, not all privileged coherent
  rewrites. It provides neither encryption nor identity authenticity.
- Independently anchored checkpoints/background monitoring are
  [Scenario E DESIGN ONLY](scenarios/scenario-e.md). Tenant authentication,
  scoped verification/export, and per-tenant chains are
  [Scenario F DESIGN ONLY](scenarios/scenario-f.md).
