# Scenario C: prototype account-access report

## Requirement and decisions

This implements only the bounded [normalized requirement](scenario-c-requirement-clarification.md):
report recorded access to exact `CLIENT_ACCOUNT` resources for a specified
inclusive UTC acceptance-time range, in deterministic JSON, with snapshot
global-chain status. It is not regulatory certification.

The implementation resolves the previously open prototype taxonomy as follows:

| Exact EventType | Report outcome | Meaning asserted by the event producer |
|---|---|---|
| `CLIENT_ACCOUNT_ACCESS_SUCCEEDED` | `succeeded` | Account-data access completed successfully. |
| `CLIENT_ACCOUNT_ACCESS_DENIED` | `denied` | Account-data access was denied. |
| `CLIENT_ACCOUNT_ACCESS_FAILED` | `failed` | Account-data access failed for a reason other than an access denial. |

Both resource type and event types are case-sensitive. No aliases, payload-based
outcome inference, or automatic classification exist. Other account events,
including updates, are excluded. Producers must deliberately emit this taxonomy;
the report does not prove their assertions, capture completeness, or that data
was actually viewed by a human. Recorded `ActorId` is preserved without inferred
identity assurance or human/service attribution. `ResourceId` is an opaque account
reference, not an ownership/client-registry lookup.

JSON is the selected format permitted by the normalized requirement's "CSV or
JSON". CSV is not included in this delivery's design or implementation. No
jurisdiction-specific obligations or legally prescribed format are invented.

## API

```http
GET /api/v1/reports/account-access?startTime=2026-10-03T00:00:00Z&endTime=2026-10-04T00:00:00Z
```

- `startTime` and `endTime` are required ISO 8601 instants: date, `T`, hours,
  minutes, seconds, optional 1-7 fractional digits, and `Z` or explicit
  `+/-HH:mm` offset. Local/offset-less timestamps are rejected.
- An offset `+` must be URL-encoded as `%2B`. Inputs are normalized to UTC.
- `startTime <= endTime`; equal endpoints select that exact instant.
- Boundaries are inclusive and compare the server-assigned audit `Timestamp`,
  not any occurrence-time field in payloads.
- Optional `resourceType` must be exactly `CLIENT_ACCOUNT`; omission selects
  that resource type. Other values return 400, not a silently empty report.
- Response: 200, `application/json; charset=utf-8`. Invalid/missing parameters
  return 400 validation Problem Details.
- A verification violation returns 200 with `chainStatus.isValid=false` and
  explicit inconsistency metadata, allowing inspection without certifying rows.
  Infrastructure failures/cancellation do not return success-shaped reports;
  unexpected failures use the existing generic 500 handler.

The endpoint is a local prototype with no regulatory authentication or
authorization workflow. It must not be exposed as a production regulator portal
without separately designed authorization, identity, and data-access safeguards.

## Response schema and determinism

Top-level fields:

| Field | Meaning |
|---|---|
| `schemaVersion` | `account-access-v1` |
| `resourceType` | `CLIENT_ACCOUNT` |
| `startTime`, `endTime` | Normalized inclusive UTC interval |
| `chainStatus` | Verification and global head from the same snapshot |
| `items` | All qualifying retained events, ascending SequenceNumber |

Each item includes `eventId`, `sequenceNumber`, `eventType`, `outcome`, `actorId`,
`resourceType`, `resourceId`, redacted `payload`, `timestamp`, `previousHash`,
`contentHash`, `isArchived`, and nullable `archivedAt`.

Chain status includes `isValid`, `eventsVerified`, `archivedEventsVerified`,
`headSequenceNumber`, `headHash`, nullable `firstInconsistentEventId`,
`firstInconsistentSequenceNumber`, `violationType`, and `detail`. Counts and head
describe the **global chain**, not just selected account-access rows.
`eventsVerified` counts the consistent prefix. Violation classification is a
string matching the existing verifier enum, not its numeric ordinal. A missing
record can have a known sequence but null identifier. An empty database has
head sequence zero, head hash `GENESIS`, and valid zero-count verification.

Deterministic serialization uses UTF-8 without BOM, recursively ordinal-sorted
property names, preserved array order, explicit nulls, and no insignificant
whitespace. Report scalar fields use System.Text.Json with the explicit
`JavaScriptEncoder.Default`; sequence/count metadata uses ordinary decimal JSON
integers. Payloads reuse [canonical JSON value rules](../canonical-hashing.md),
including normalized exact scientific-number spelling and canonical Unicode
escaping. All report
timestamps are UTC `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'`. There is no generation-time
field, random report identifier, or culture-dependent formatting.

For identical interval instants, effective redaction policy, selected data,
archive state, and verification snapshot, bytes are identical. A new append or
archive change may change the metadata/output even if the selected rows do not
change. Canonical payload scientific notation is valid JSON: consumers should
parse payload numbers rather than depend on source numeric spelling.

An empty report has `items: []` and complete interval/chain metadata. Sequence
gaps between report items are normal when nonqualifying global records are
excluded; they do not independently establish a chain violation.

## Snapshot and integrity design

`IAccountAccessReportService` is implemented in the existing Infrastructure
project. A PostgreSQL repeatable-read transaction reads global head, verifies the
entire retained chain using the existing verification rules, selects report rows,
and reads archive metadata from one consistent snapshot. It does not acquire the
append advisory lock or modify audit events.

The existing verification implementation exposes a shared internal snapshot
operation so the report does not use a second connection/snapshot or duplicate
verification rules. Standalone verification retains its own repeatable-read
transaction and unchanged behavior. Writes accepted after the report snapshot
are outside its declared head/verification boundary.

Archive state does not exclude events or change their hashes. Report payloads
are derived directly from immutable event data through the existing protector,
rather than trusting separately mutable saved read projections. Configured
legacy sensitive values and all commitment envelopes are masked. Original
commitment values cannot be restored. Unconfigured fields and event identifiers
still require privacy review.

Redacted report payloads cannot reproduce ContentHash; this report is not an
offline-verifiable evidence bundle. Whole-chain consistency is not proof of
authentic identity, lawful access, complete capture, or legal compliance.

## Tests and validation

Unit tests cover the exact allowlist/exclusions, range/resource validation, UTC
normalization, deterministic nested JSON across input property order and
cultures, explicit nulls, and Unicode escaping.

PostgreSQL/API tests cover inclusive and equal boundaries, exclusion just outside
boundaries, all three access outcomes, unrelated event/resource exclusion,
ascending global sequence order, archived inclusion, redacted legacy projection,
repeat-byte determinism, offset-equivalent intervals, empty/global status,
invalid input, a chain violation outside selected rows, a deleted record with
missing-record classification, OpenAPI discovery, and a concurrent append while
the report snapshot is held open. Ordering tests deliberately use timestamps
that differ from sequence order.

Run against the isolated integration-test database:

```powershell
dotnet build AuditLogService.sln
dotnet test AuditLogService.sln
```

Validation: the solution build succeeded and the full test runner passed all
146 tests with zero failures. Scenario C adds 8 unit and 11 PostgreSQL/API
integration cases. No database migration or development-database mutation was
required. These results are prototype evidence, not jurisdiction-specific
certification.

## Boundaries and limitations

- No CSV, bulk evidence bundle, signatures, external trusted checkpoints,
  regulator approval, legal certification, or regulatory identity workflow.
- No client/account registry, identity resolution, new retention rules, physical
  deletion, or modification of chained content.
- No inference of access from arbitrary existing event names.
- This bounded prototype loads the global chain and report into memory; it has
  no new pagination or maximum-range policy. Production sizing and streaming are
  future design concerns, not silently truncated results.
- Tampering with a chained record can be detected when the affected record and
  subsequent chain relationships are verified. A privileged attacker replacing
  the entire self-consistent chain still requires independently trusted evidence
  to detect; the report does not eliminate this existing limitation.
