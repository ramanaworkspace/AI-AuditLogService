# Scenario B: retention and archival only

## Requirement and scope

Records older than a configurable retention window become eligible for archival.
Archive state must not change immutable event content or break verification.
This prototype marks events archived in a separate table and retains every
original event in the global chain. There is no physical deletion.

Only retention is implemented here. Redaction, export, compliance reporting,
scheduled jobs, and a new HTTP archive-operation endpoint are outside this task.
Archival is an explicit service operation, not automatic deletion or a timer.

## Retention rule

An existing event is age-eligible when:

```text
event.Timestamp < serverUtcNow - Retention.Window
```

The comparison is strict: an event exactly at the cutoff is not eligible.
The timestamp is the authoritative server acceptance time, not a payload-supplied
occurrence date. Each operation captures the injected `TimeProvider` once.
Server evaluation/archival time is microsecond-aligned for PostgreSQL.

The configurable default is **90 days**, a prototype choice rather than an
assessment-mandated policy:

```json
{
  "Retention": {
    "Window": "90.00:00:00"
  }
}
```

Set `Retention__Window` to override configuration through an environment variable.
The format is an invariant .NET `TimeSpan`, for example `30.00:00:00` for 30 days.
Zero/negative windows fail options validation at application startup. Malformed
values fail explicitly rather than silently falling back. The service also
guards invalid/underflowing windows when instantiated outside application DI.
Sub-microsecond window values preserve the strict comparison: the SQL cutoff
is rounded upward to the next representable microsecond boundary, while the
reported cutoff remains the exact calculated value.

Increasing the window later does not unarchive existing records. Age eligibility
and actual archived state are separate concepts; a record can remain archived
after a policy change even if it is no longer age-eligible.

## Archive operation and state

The DI-registered
[IAuditEventRetentionService](../../src/AuditLogService.Application/Retention/IAuditEventRetentionService.cs)
provides:

- `GetStateAsync(eventId)`: event ID, current age eligibility, nullable ArchivedAt,
  and derived IsArchived. A missing event throws KeyNotFoundException; an orphaned
  archive marker never makes an absent event look legitimate.
- `ArchiveEligibleAsync()`: archives all eligible events visible to one PostgreSQL
  statement and returns the eligibility cutoff, operation timestamp, and count
  of newly archived records.

Usage from an application/service scope:

```csharp
var retention = scope.ServiceProvider.GetRequiredService<IAuditEventRetentionService>();
var result = await retention.ArchiveEligibleAsync(cancellationToken);
var state = await retention.GetStateAsync(eventId, cancellationToken);
```

The operation is a parameterized `INSERT ... SELECT ... ON CONFLICT DO NOTHING`
into `audit_event_archives`. PostgreSQL makes the single statement atomic.
The table has:

- `event_id`: primary key and one-to-one foreign key to `audit_events.event_id`;
- `archived_at`: non-null UTC `timestamp with time zone`.

The foreign key uses RESTRICT, not cascading deletion. A missing archive row
means active; a row means archived. Repeated/concurrent runs cannot create
duplicate markers or overwrite the first archive timestamp. Failures propagate
to callers; the operation never reports a successful archive count on failure.
Cancellation is forwarded to database calls.

The operation only inserts archive markers. It never updates any of EventId,
SequenceNumber, EventType, ActorId, ResourceType, ResourceId, Payload, Timestamp,
PreviousHash, ContentHash, or chain-head metadata. The append-only event trigger
and hash algorithm remain unchanged.

## Why archive state is outside the hash

Archive status is lifecycle metadata, not part of what happened. Putting it in
canonical event content would make a legitimate retention operation invalidate
the original content hash and subsequent chain relationships.

The separate table lets retention change lifecycle state without rewriting audit
history. No archive flags are added to the domain event or canonical serializer.
Archive metadata itself is not cryptographically authenticated by the event hash;
database access control is required to protect it.

## Query and verification behavior

Existing GET query results still include active and archived events, using the
same filtering, ordering, and pagination. Each returned item additionally exposes:

```json
{
  "isArchived": true,
  "archivedAt": "2026-10-03T12:00:00+00:00"
}
```

Active/new events have `isArchived: false` and `archivedAt: null`.
Archival does not hide payloads, move data to cold storage, or change access rights.

Verification continues to read all original event rows, including archived ones,
in its existing repeatable-read snapshot. It does not skip archived positions or
treat archive markers as replacements for missing events.

The verification response additionally includes `archivedEventsVerified`: the
number of archived events in the successfully verified prefix. For an intact
chain, this is the archived count across the entire verified chain. On failure,
the inconsistent event and later records are excluded from that count.

| Observed state | Verification outcome |
|---|---|
| Original archived event remains intact | Valid; counted in archivedEventsVerified |
| Archived event is modified directly | ContentHashMismatch or the applicable link/sequence violation |
| Original event is absent, even with an archive marker | MissingRecord, using existing head comparison |

Normal database deletion is blocked by the append-only event trigger and foreign
key. Privileged missing-record tests intentionally bypass triggers inside a test
transaction; this does not weaken production behavior.

## Why physical deletion is deferred

Removing hashed records discards material needed for full-chain verification.
An archive marker alone cannot recompute the missing content hash or reconstruct
the original identifier and payload. Retaining immutable rows preserves ordering,
predecessor links, and verification evidence.

This is logical archival only and does not reduce database storage or provide
privacy erasure. Cold storage, independently anchored proofs, legal deletion
policies, redaction, and lifecycle authorization require separate designs.

## Migration and validation

The additive `AddAuditEventArchives` EF migration creates only the archive table
and its constraints. It does not alter or backfill audit event content. Existing
records remain active until the service operation is invoked.

Apply migrations to the same intended database used by the running API. The
existing design-time factory reads `AUDITLOG_CONNECTION_STRING`; runtime reads
`ConnectionStrings:AuditLogDatabase`. Their defaults differ, so set explicit
matching connection strings before running:

```powershell
dotnet ef database update --project src\AuditLogService.Infrastructure
```

No development database was migrated as part of this task; integration tests
apply migrations to their dedicated PostgreSQL database.

Tests in
[AuditEventRetentionTests](../../tests/AuditLogService.IntegrationTests/Retention/AuditEventRetentionTests.cs)
cover strict boundaries, configurable windows, advancing time, sub-microsecond
comparison, empty/no-eligible operations, invalid options, cancellation, missing
event state, repeat/concurrent idempotency, preservation of every canonical field
and ContentHash, unchanged chain head, query representation, and API verification
of intact/missing/tampered archived events.

Validation on 2026-10-03:

- Targeted retention tests: **14 passed, 0 failed**.
- Full solution build: **succeeded**.
- All tests: **93 passed, 0 failed** (35 unit and 58 integration tests).
- EF model consistency check: **no pending model changes**.
- `git diff --check`: passed.

The installed EF CLI is 8.0.0 and reports a version warning against the 10.0.0
runtime; migration generation and model checking nevertheless completed
successfully, and tests applied the migration successfully. Tool upgrades were
not included in this task.

Reproduce against a disposable dedicated PostgreSQL test database:

```powershell
docker compose up -d postgres-test
dotnet build AuditLogService.sln
dotnet test AuditLogService.sln --no-build
```

## Limitations

- The service must be invoked explicitly; no scheduler or retention endpoint is
  added by this slice.
- Bulk archival is one set-based statement. Very large archives may require
  batching and operational controls; no performance claims are made.
- Archive markers are lifecycle information, not independent proof that a policy
  was authorized. Privileged database changes remain a threat.
- Existing Scenario A verification limitations remain: same-database head metadata,
  full-history memory usage, and domain materialization of malformed stored values.
- No redaction, export, compliance reporting, load testing, or physical deletion
  was implemented.
