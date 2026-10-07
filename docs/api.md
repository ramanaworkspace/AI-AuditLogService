# API contract: current implementation

Source of truth: [endpoint mappings](../src/AuditLogService.Api/Endpoints/)
and [contracts](../src/AuditLogService.Api/Contracts/). All examples below are
request examples, not newly executed requests or fabricated result evidence.
The local load run used `http://localhost:5115`; select your running API address.

**IMPLEMENTED:** append, query, full verification, normalized account report,
health, and development OpenAPI. **PARTIALLY IMPLEMENTED:** safe export projector
only. **DESIGN ONLY:** E/F operational and tenant controls. **OUT OF SCOPE:**
update/delete, physical deletion, CSV report, and legal certification.

There is no authentication/authorization. Do not expose these endpoints as a
production or tenant-isolated service.

## POST /api/v1/audit-events

Content-Type: `application/json`.

```json
{
  "eventType": "CLIENT_ACCOUNT_ACCESS_SUCCEEDED",
  "actorId": "service-example",
  "resourceType": "CLIENT_ACCOUNT",
  "resourceId": "account-example",
  "payload": {
    "operation": "read",
    "accountNumber": "synthetic-example"
  }
}
```

| Property | Contract |
|---|---|
| `eventType`, `actorId`, `resourceType`, `resourceId` | Required nonblank strings |
| `payload` | Required JSON object; nested arrays/scalars/nulls allowed |

EventId, SequenceNumber, Timestamp, PreviousHash, and ContentHash are assigned
by the server. Unknown top-level JSON properties, including attempted chain
overrides, are ignored rather than rejected. The timestamp is UTC acceptance
time, truncated to microseconds before hashing; it is not a client occurrence
timestamp. Payload occurrence-time fields have no chain-order authority.

Configured sensitive paths are committed before storage and masked in the
response. Duplicate payload property names or the reserved `$auditCommitment`
marker anywhere in input are rejected.

**201 Created:** body is an event response (below). Location is
`/api/v1/audit-events/{eventId}`. **There is no single-event GET route at this
Location.** Use the list/query route.

**400:** missing/blank fields, non-object payload, malformed JSON/binding, or
reserved/ambiguous payload. Explicit validation uses Problem Details with an
`errors` dictionary; framework binding errors may not have the same dictionary.
**500:** unexpected failures use generic Problem Details. Database string
lengths (200/300/200/500 respectively) are not explicit POST length validators;
do not assume oversized strings receive a tailored 400.

No idempotency key or automatic retry exists. Repeating a successful logical
request creates another event; an uncertain COMMIT acknowledgment needs
reconciliation, not a blind exactly-once assumption.

## Event response

POST and query items use web-default camel-case JSON:

| Field | Meaning |
|---|---|
| `eventId` | Server-generated GUID |
| `sequenceNumber` | Positive global committed sequence |
| `eventType`, `actorId`, `resourceType`, `resourceId` | Recorded producer assertions |
| `payload` | Redacted read representation, not immutable hash input |
| `timestamp` | Authoritative UTC acceptance instant |
| `previousHash` | `GENESIS` or preceding lowercase SHA-256 digest |
| `contentHash` | Immutable canonical record digest |
| `archivedAt` | Nullable archive timestamp |
| `isArchived` | Derived from archive timestamp; false for newly appended rows |

Ordinary API timestamp rendering uses System.Text.Json DateTimeOffset
serialization; it is not the fixed canonical hash timestamp representation.
Hash verification cannot be reproduced from a masked response payload alone.

## GET /api/v1/audit-events

All filters are optional and combine with AND:

| Parameter | Behavior |
|---|---|
| `actorId` | Exact stored actor match |
| `resourceType` | Exact stored resource type match |
| `resourceId` | Exact stored resource ID match |
| `eventType` | Exact stored event type match |
| `startTimestamp` | Inclusive lower Timestamp bound |
| `endTimestamp` | Inclusive upper Timestamp bound |
| `limit` | Default 50; allowed 1 through 500 |
| `cursor` | Previous response's `nextCursor` |

Timestamps use ASP.NET DateTimeOffset binding and UTC-normalized comparisons.
Supply explicit ISO 8601 offsets to avoid ambiguity; this query route does not
use the report route's stricter explicit-offset parser.

```http
GET /api/v1/audit-events?resourceType=CLIENT_ACCOUNT&limit=50&startTimestamp=2026-10-03T00:00:00Z
```

**200:** `{ "items": [...], "nextCursor": "<token-or-null>" }`.
Items ascend by SequenceNumber and include archived rows. No total count is
returned. `nextCursor` is null when the fetched page has no further matching row.

Pagination is keyset-based (`SequenceNumber > cursor sequence`). Retain all
filters between pages and URL-encode the cursor. The Base64 sequence token is
neither encrypted nor authenticated nor bound to filters/identity. It is not an
access-control mechanism. Concurrent appends do not shift earlier sequence
positions, but multiple HTTP pages do not constitute one frozen snapshot.
Archive/projection metadata reads also are not one multi-page snapshot.

**400:** invalid cursor, limit outside 1..500, reversed interval, or invalid
typed query binding. Unknown query parameters have no defined filtering effect.
Unexpected database/projection consistency failures return generic 500, not
silently substituted successful results.

## GET /api/v1/audit-events/verify

No parameters. Walks the full global retained chain, including archived events,
against head metadata in one repeatable-read snapshot.

**200** for either a valid chain or a detected inconsistency:

| Field | Meaning |
|---|---|
| `isValid` | Full snapshot consistency result |
| `eventsVerified` | Count in the successfully verified prefix |
| `archivedEventsVerified` | Archived records in that prefix |
| `firstInconsistentEventId` | Nullable GUID; unknown for a missing record |
| `firstInconsistentSequenceNumber` | Nullable sequence |
| `violationType` | Nullable numeric enum, listed below |
| `detail` | Nullable explanatory text |

| Numeric value | Violation |
|---:|---|
| 0 | ContentHashMismatch |
| 1 | PreviousHashMismatch |
| 2 | SequenceGap |
| 3 | DuplicateSequenceNumber |
| 4 | InvalidGenesisRelationship |
| 5 | MissingRecord |
| 6 | ChainHeadMismatch |

Valid results have null inconsistency fields. Empty storage with head
`(0, GENESIS)` is valid. Missing records are classified using retained head/count
evidence; classifications do not prove the cause of corruption. Link checks
precede content checks. Infrastructure errors do not return a valid status.

This endpoint is not paginated, streaming, background verification, or an
independently anchored integrity proof.

## GET /api/v1/reports/account-access

```http
GET /api/v1/reports/account-access?startTime=2026-10-03T00:00:00Z&endTime=2026-10-04T00:00:00Z
```

| Parameter | Contract |
|---|---|
| `startTime`, `endTime` | Required ISO 8601 date/T/time with seconds, optional fractional digits, and `Z` or `+/-HH:mm` |
| `resourceType` | Optional; only exact `CLIENT_ACCOUNT` accepted |

Offsets normalize to UTC. URL-encode offset `+` as `%2B`. Bounds are inclusive
and `startTime <= endTime`; equal endpoints select that instant. Offset-less,
invalid/missing dates, reversed ranges, and unsupported resources return 400
validation Problem Details.

Only these exact EventTypes qualify:

- `CLIENT_ACCOUNT_ACCESS_SUCCEEDED` -> `succeeded`
- `CLIENT_ACCOUNT_ACCESS_DENIED` -> `denied`
- `CLIENT_ACCOUNT_ACCESS_FAILED` -> `failed`

There is no inferred classification, alias, actor filter, arbitrary resource
selection, format selector, CSV output, or report pagination.

**200**, `application/json; charset=utf-8`:

- `schemaVersion`: `account-access-v1`.
- `resourceType`: `CLIENT_ACCOUNT`.
- `startTime`, `endTime`: normalized UTC text.
- `items`: all qualifying rows ascending by global sequence; empty array for
  no matches. Each has the event response fields plus `outcome`.
- `chainStatus`: `isValid`, `eventsVerified`, `archivedEventsVerified`,
  `headSequenceNumber`, `headHash`, `firstInconsistentEventId`,
  `firstInconsistentSequenceNumber`, `violationType`, `detail`.

Unlike the verify API, report `violationType` is an enum-name **string** or null.
Chain counts/status describe the global snapshot, not only report items.
Corruption returns 200 with invalid chain status, not a certified report.

Output is deterministic for the same interval instants, data snapshot, archive
state, and redaction policy: sorted JSON object names, fixed UTC timestamp text,
explicit nulls, UTF-8, no insignificant whitespace; payloads use canonical value
rules. Appends/archival can change output. The endpoint returns a whole
in-memory JSON response; no row/range limit is currently enforced.

## Infrastructure and absent routes

- GET `/health`: basic registered health framework endpoint, without a
  database readiness or chain integrity check.
- GET `/openapi/v1.json`: mapped only in Development; no Swagger UI is mapped.
- No PUT/PATCH/DELETE event routes.
- No archive operation or bulk verifiable export endpoint.
- No E scheduler/checkpoint/metrics endpoints or F tenant routes.

See [security and limitations](architecture.md#security-and-limitations)
before deploying or consuming this prototype.
