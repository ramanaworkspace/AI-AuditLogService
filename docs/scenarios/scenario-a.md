# Scenario A: core audit log and tampering proof

## Requirement and scope

Record an append-only audit history with event type, actor, resource type and ID,
structured payload, and timestamp. Provide write, filtered query, and full-chain
verification APIs. Demonstrate that direct PostgreSQL mutation is detected,
identify the earliest inconsistency, and classify the violation.

This document covers Scenario A only. Retention, redaction, export, compliance
reporting, load testing, background verification, and tenant isolation are not
implemented by this task.

## Design

The existing .NET 10 solution separates Minimal API contracts, application use
cases, immutable domain events/canonical hashing, and EF Core/Npgsql persistence.
PostgreSQL stores structured payloads as JSONB. Event IDs and sequence numbers
are unique. An append-only trigger rejects ordinary UPDATE and DELETE operations.
This is a safeguard, not protection against a privileged database administrator.

Each append runs in one transaction: acquire the global transaction-scoped
advisory lock, read the chain head, assign the next sequence and predecessor,
construct and hash the event, insert it, update the head, and commit. Rollback
discards both changes. See [concurrency](../concurrency.md) for the single global
write bottleneck and horizontal scalability trade-off.

The server assigns EventId, SequenceNumber, Timestamp, PreviousHash, and
ContentHash. UTC timestamps are truncated to PostgreSQL microsecond precision
before hashing so persisted values round-trip without changing the digest.

## API

### POST `/api/v1/audit-events`

Request:

```json
{
  "eventType": "document.created",
  "actorId": "actor-1",
  "resourceType": "document",
  "resourceId": "doc-1",
  "payload": { "title": "Original" }
}
```

Returns 201 with the persisted event's fields and server-assigned chain fields.
Missing/blank required strings and non-object payloads return 400 validation
problems. Client-supplied chain fields are ignored, not used. There is no update
or delete endpoint.

### GET `/api/v1/audit-events`

Supported query parameters: `actorId`, `resourceType`, `resourceId`, `eventType`,
`startTimestamp`, `endTimestamp`, `limit`, and `cursor`. Time boundaries are
inclusive. Sequence-ordered keyset pagination returns `items` and `nextCursor`.
The default limit is 50; allowed limits are 1 through 500. Invalid cursor, limit,
and reversed time ranges return 400. Callers must retain their filters between
pages. Pagination is stable against insertions but is not a frozen snapshot of
the chain across multiple HTTP requests.

### GET `/api/v1/audit-events/verify`

Returns 200 for both intact and inconsistent chains:

```json
{
  "isValid": false,
  "eventsVerified": 1,
  "firstInconsistentEventId": null,
  "firstInconsistentSequenceNumber": 2,
  "violationType": 5,
  "detail": "Expected record at sequence number 2 is absent relative to the persisted chain head; its event identifier is unavailable."
}
```

The existing API serializes violation enums numerically. `eventsVerified` counts
the consistent prefix. Valid results have null inconsistency fields.

## Hash chain

SHA-256 hashes the canonical UTF-8 JSON containing, in fixed order:
`eventId`, `sequenceNumber`, `eventType`, `actorId`, `resourceType`, `resourceId`,
`payload`, `timestamp`, and `previousHash`. `contentHash` is excluded from its
own input. Payload object keys are recursively sorted, array order is preserved,
numbers are normalized exactly, and nested nulls are retained.

The first record has `previousHash = "GENESIS"`; each later record has the
preceding record's lowercase hexadecimal content hash. Canonical timestamps have
seven fractional digits (the appended timestamp's last digit is zero).
See [canonical hashing](../canonical-hashing.md) for the complete byte-level rules.

## Verification and classification

Verification reads events and chain-head metadata in a PostgreSQL repeatable-read
transaction, ensuring both represent the same snapshot despite concurrent
appends. It checks ordered records, stopping at the first observed inconsistency:
duplicate sequence, gap, genesis/predecessor linkage, and recomputed content hash.
It then checks the verified tip against the head.

| Category | Violation | JSON value | Meaning |
|---|---|---|---|
| Content tampering | ContentHashMismatch | 0 | Stored digest differs from canonical event content |
| Chain-link tampering | PreviousHashMismatch | 1 | Predecessor differs from the preceding event's digest |
| Sequence failure | SequenceGap | 2 | Unexpected sequence without evidence of a reduced record count relative to the head |
| Sequence failure | DuplicateSequenceNumber | 3 | Repeated sequence; normally prevented by PostgreSQL uniqueness |
| Chain-link tampering | InvalidGenesisRelationship | 4 | First record does not link to GENESIS |
| Missing data | MissingRecord | 5 | Expected record absent relative to the persisted head |
| Head inconsistency | ChainHeadMismatch | 6 | Internally verified tip disagrees with head metadata |

For a gap with fewer stored records than the expected head sequence, the service
reports MissingRecord at the earliest expected missing sequence. The deleted
record's identifier is unavailable, so it is null rather than fabricated.
Tail deletion and deletion of all events are also detected when head metadata
remains. A sequence edit retaining the record count reports SequenceGap and the
first surviving record that violates sequence order. Link checks precede hash
checks, so a PreviousHash edit reports a link violation even though it also
changes the canonical hash. Earlier content corruption takes priority over a
later missing record.

These classifications describe observed evidence, not proof of the attacker's
actions. Combined mutations or altered metadata can make the original cause
ambiguous.

## Integration test evidence

[AuditEventTamperingTests](../../tests/AuditLogService.IntegrationTests/Api/AuditEventTamperingTests.cs)
uses the real HTTP pipeline and a dedicated PostgreSQL database. Each case
first POSTs three events and verifies a valid chain, then directly mutates the
store and verifies again. Assertions cover validity, consistent-prefix count,
earliest event identifier or missing sequence, and violation classification.

Coverage:

- Payload and actor modification: ContentHashMismatch.
- ContentHash modification: ContentHashMismatch.
- PreviousHash modification and pointing to the wrong existing predecessor:
  PreviousHashMismatch.
- First record's predecessor modification: InvalidGenesisRelationship.
- Sequence 3 changed to 5: SequenceGap.
- First, middle, and tail deletion, plus deletion of the entire chain:
  MissingRecord.
- Multiple content mutations: earliest record reported, not last mutation.
- Head hash corruption: ChainHeadMismatch.
- Content corruption preceding deletion: earliest content failure wins.

Tests bypass the append-only trigger only inside a privileged test transaction,
using `SET LOCAL session_replication_role = replica`; commit or rollback restores
the setting. Production triggers and constraints are unchanged. Never run these
tests against a development or production database: the fixture resets its
configured database. The default test database is `audit_log_test` on port 5433;
`AUDITLOG_TEST_CONNECTION_STRING` must target a disposable dedicated test store.

Before the fix, the initial 12 cases produced 8 passes and 4 failures: first/middle
deletions lacked missing-record classification, while tail/all deletion incorrectly
verified as valid. This evidence justified the production verification change.

Final validation on 2026-10-03:

- Full solution build (`dotnet build AuditLogService.sln`): succeeded.
- All tests through VS Code's test runner: **79 passed, 0 failed**.
- The new tampering class contributes 14 passing cases to the existing 65 tests.
  No load test was executed or claimed.

To reproduce with the existing xUnit v2/VSTest setup:

```powershell
docker compose up -d postgres-test
dotnet build AuditLogService.sln
dotnet test AuditLogService.sln --no-build
```

## Limitations

- Tampering with a chained record can be detected when the affected record and
  subsequent chain relationships are verified. The system is not tamper-proof.
- Head metadata is in the same database, not an independently trusted checkpoint.
  Rewriting both records and metadata consistently can evade detection. No
  signature, external anchor, or deleted event-ID recovery is provided.
- Verification loads the whole history into memory and performs a linear scan.
  Large-history monitoring/scaling remains future scope.
- Stored values violating domain invariants may throw during domain materialization
  rather than return a structured violation. The mutation tests retain valid
  field formats and database constraints.
- Duplicate sequences are covered by unit verification tests and database rejection,
  not by disabling unique constraints in the API tampering tests.
- Known API length/duplicate-payload validation and development configuration
  consistency gaps from the prior review remain outside this tampering-proof task.
