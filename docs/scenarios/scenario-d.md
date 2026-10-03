# Scenario D: concurrent append correctness

## Concurrency problem and selected solution

Two uncoordinated writers could read the same global head and both choose the
same next sequence and predecessor, causing duplicate order positions or a fork.
The selected architecture remains unchanged:

- One global chain and one persisted chain-head row.
- The fixed PostgreSQL transaction-scoped advisory lock,
  `pg_advisory_xact_lock(713204890501)`.
- A transaction that acquires the lock, reads the head, constructs/hashes the
  event, inserts the event and redacted projection, advances the head, and commits.

There is no new queue, sharded chain, in-memory correctness lock, or automatic
retry policy. See [concurrency design](../concurrency.md) and
[canonical hashing](../canonical-hashing.md). This delivery changes only tests
and documentation.

## Correctness model

For an initially empty test database and N successful append acknowledgements:

1. Every acknowledged EventId is present exactly once; every persisted event
   corresponds to an acknowledgement in the tested successful workload.
2. Ordered sequence numbers are exactly `1..N`, with no duplicates or gaps.
3. Event identifiers, predecessor hashes, and content hashes are unique in the
   tested chain. GENESIS occurs only as the first predecessor.
4. Each later PreviousHash equals the immediately preceding ContentHash.
   This, continuity, and unique predecessors exclude a fork in the tested data.
5. Every persisted ContentHash recomputes correctly from immutable event content.
6. Persisted identities, sequence numbers, hashes, actor/request tokens, and
   payload request tokens reconcile with the append receipts.
7. Every event has one matching redacted projection; there are no orphan
   projections from failed transactions.
8. Head metadata equals the final event's sequence/hash, and the production
   full-chain verifier reports valid with N verified events.

Database uniqueness constraints reinforce identity/sequence correctness. The
advisory-lock protocol provides predecessor serialization; uniqueness constraints
alone do not establish the entire correctness model. Every production writer must
follow the same protocol. Privileged direct-store modifications are outside this
append contract and addressed by tamper-evidence tests.

## Repeatable concurrent test

[ScenarioDCorrectnessTests](../../tests/AuditLogService.IntegrationTests/Concurrency/ScenarioDCorrectnessTests.cs)
uses the existing isolated PostgreSQL fixture and serialized xUnit database
collection. Each of three independent theory cases resets the test chain:

- Start **50 writers**, each with its own append service and fresh context per
  append, using an asynchronous common start gate.
- Hold the real global advisory lock in a separate PostgreSQL transaction.
- Observe **50 database sessions actually waiting on the advisory lock** in
  `pg_stat_activity`, identified by a per-run application name. The test does not
  count merely queued .NET tasks as database contention.
- Release the database blocker. Each writer performs three sequential appends,
  yielding **150 acknowledged writes per case**; writers execute concurrently.
- Query every event, order by sequence, reconcile receipts, check all invariants
  above, and run the production full verifier.

The test connection pool allows 60 connections for these writers; the database
must have capacity for 50 writer sessions plus monitoring, blocker, and ordinary
test connections. A bounded 90-second readiness deadline and 180-second writer
command timeout detect setup/failure hangs, not performance objectives.
The monitor uses a separate connection outside the blocker transaction so
PostgreSQL's statistics snapshot does not freeze the observed waiter count.

No ordering between writer identities is promised. Only committed global sequence
order is authoritative. A failed writer fails the test; errors are not counted as
successful writes or silently retried.

## Failure model and recovery evidence

| Case | Injection and expected result |
|---|---|
| Failed insert | A temporary PostgreSQL BEFORE INSERT trigger raises SQLSTATE `P0001`. The exception propagates, and events, projections, and head remain at the previous committed state. |
| Failed chain-head update | A temporary BEFORE UPDATE trigger on chain metadata raises `P0001`. Regardless of EF statement ordering, the complete append transaction is rolled back. |
| Rollback after SaveChanges | A test-only EF transaction interceptor throws immediately before CommitAsync. SaveChanges has completed, but its event/projection/head changes must all roll back. |
| Connection establishment failure | Connect to a uniquely named nonexistent database on the same PostgreSQL server, with pooling disabled. SQLSTATE `3D000` propagates and the real test chain is unchanged. No other server is stopped or modified. |
| Explicit retry after known rollback | Remove the trigger/use a healthy context and resubmit. The next committed sequence is previous head + 1 and links to the previous head hash, with no leaked advisory lock or partial row. |
| Already committed request repeated | Submit the same logical request twice. Both commits are present with different EventIds and adjacent sequence numbers; the service does not claim idempotency. |
| Application recovery | Commit through an API host, dispose it, create a fresh API host against the same database, and append again. The new host recovers the persisted head and verifies both events, without in-memory chain state. |

Failure injections are local to the test factory or temporary database triggers.
Triggers/functions are removed in `finally`; the advisory blocker is rolled back
in `finally`, writer tasks are awaited, and only each run's uniquely named writer
connection pool is cleared so repeated cases do not retain 50 idle sessions each.
The production append path, schema,
and configuration are not changed.

### Retry boundaries

The implementation does not enable automatic retries. Known rejected statements,
pre-commit failures, and connection-establishment failures are safe to resubmit
after confirming rollback and correcting the cause. Tests exercise explicit
caller retry, not a hidden retry mechanism.

A network failure during/after COMMIT can leave the caller uncertain whether the
transaction committed. Blind retry can append the logical event twice because
EventId is server-assigned and there is no idempotency key. The repeated-request
test makes that existing boundary explicit. The suite does not simulate an
ambiguous commit acknowledgement or claim exactly-once logical request delivery.
Successful append receipts are reconciled with durable rows in the tested runs;
this is distinct from proving every caller-observed error means no commit.

### Practical limits of recovery tests

Host recreation is an orderly in-process WebApplicationFactory restart, not an
OS process kill or PostgreSQL crash/recovery exercise. Connection failure uses
a nonexistent database, not a production outage or TCP partition. The suite
does not terminate unrelated sessions, stop shared databases, or test replication
failover, disk/power loss, stale backups, or database durability configurations.
PostgreSQL transaction/session cleanup releases transaction-scoped locks when
the transaction ends or a disconnected session is detected; failure detection
can take time and is not a guarantee of instantaneous availability.

## Trade-offs

- Strong/simple global ordering and one linear verification pass.
- Atomic event/projection/head state and automatic transaction lock release.
- Database-level coordination across independent callers rather than per-process
  state. The suite exercises many independent connections and recreated hosts,
  not a multi-machine deployment.
- Single global write bottleneck: horizontal write scalability is deliberately
  sacrificed. Additional writers wait rather than increase serialized write
  capacity.
- Connection/lock waiting, statement timeouts, and unavailable PostgreSQL can
  surface errors. They are not silently converted into successful writes.
- Hash-chain consistency is tamper-evidence, not tamper prevention,
  authenticity, or independent protection against whole-chain replacement.

## Running and interpreting evidence

Use only the dedicated integration-test database. The fixture resets events,
projections, archives, and head metadata; never point this suite at development
or production data. SQL trigger creation and activity inspection require suitable
permissions. A normally interrupted runner may leave test-only trigger/function
objects; inspect and remove only those named `scenario_d_reject` and
`scenario_d_reject_write` in the isolated database before rerunning.

```powershell
dotnet test tests\AuditLogService.IntegrationTests\AuditLogService.IntegrationTests.csproj --filter FullyQualifiedName~ScenarioDCorrectnessTests
dotnet test AuditLogService.sln
```

Validation evidence:

- All nine Scenario D integration cases passed, including three repetitions
  observing 50 advisory-lock waiters and verifying 150 committed events each.
- The solution build succeeded; the complete test runner passed **155 tests
  with zero failures** after final test-pool cleanup was included.
- An earlier run with a 60-second writer command timeout failed two contention
  cases with lock-wait timeouts. Only test wait limits were increased; no
  production behavior or hidden retry was introduced. The final evidence is
  bounded correctness under the documented harness settings, not a timeout or
  performance guarantee.

These are correctness tests, not a load benchmark. No throughput, p95 latency,
hardware capacity, or performance improvement is claimed or fabricated.

## Real HTTP load-test harness

The existing [LoadTests project](../../tests/AuditLogService.LoadTests/) now runs
an actual finite HTTP workload against a separately running API and PostgreSQL.
It does not use WebApplicationFactory, mock storage, or bypass the append API.
Production architecture is unchanged.

### Execute exactly

From the repository root, start the dedicated local PostgreSQL test service using
the [README setup](../../README.md). Do not run integration tests concurrently:
their fixture resets the same test database. Ensure no other client writes during
measurement. The harness never truncates, deletes, or migrates data; its synthetic
events remain in the selected database.

In the first PowerShell terminal:

```powershell
# Supply the dedicated test connection, not the development database.
# Existing local test service: Host=localhost;Port=5433;Database=audit_log_test
$env:ConnectionStrings__AuditLogDatabase = Read-Host 'Dedicated PostgreSQL test connection string'
$env:AUDITLOG_CONNECTION_STRING = $env:ConnectionStrings__AuditLogDatabase
dotnet build AuditLogService.sln
dotnet ef database update --project src\AuditLogService.Infrastructure --startup-project src\AuditLogService.Infrastructure --no-build
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-build --no-launch-profile --project src\AuditLogService.Api --urls http://localhost:5115
```

The Infrastructure design-time factory uses `AUDITLOG_CONNECTION_STRING`.
The existing EF CLI 8/runtime 10 version warning is known; this does not change
the runtime stack. Migration command must succeed before starting the API.
Stop a server you started with Ctrl+C when finished; do not stop an unrelated
process.

In a second PowerShell terminal:

```powershell
Invoke-RestMethod http://localhost:5115/health
Invoke-RestMethod http://localhost:5115/api/v1/audit-events/verify
dotnet run --no-build --project tests\AuditLogService.LoadTests -- http://localhost:5115 50 10 180 docs\scenarios\results
$LASTEXITCODE
```

Arguments, in order:

1. API base URL (HTTP or HTTPS).
2. Parallel clients, minimum 50.
3. Writes per client, minimum 1.
4. Per-request timeout in seconds, minimum 1.
5. Output directory.

Each client sends its own sequential writes after a common asynchronous start
gate; all clients execute concurrently with up to that many HTTP connections.
There are no automatic retries, warm-up requests, or throttling sleeps.
Exit 0 means every planned request succeeded and validation passed; 1 means
request/validation failure; 2 means invalid CLI arguments.

Artifacts named `load-<runId>.json` and `load-<runId>.md` are written to the output
directory. JSON contains raw attempts, status/errors, per-request latency and
receipts, aggregate metrics, final verifier response, and validation errors;
Markdown and console output provide a readable summary. Report files contain
synthetic identifiers for this run, not database credentials. Treat output as
sensitive if future workload data differs.

### Measurement definitions

- Total requests counts completed attempts, not the configured planned number;
  both values are emitted.
- Successful writes require HTTP **201 Created** and a parsed append receipt.
  Failed writes count other responses, transport/timeouts, or invalid receipts.
- Elapsed time uses `Stopwatch`, from release of the start gate until all append
  attempts finish, excluding baseline/final pagination and verification.
- Throughput is acknowledged successful writes divided by that elapsed time.
- p95 append latency is the nearest-rank 95th percentile of successful request
  durations, including connection waiting, server handling, response transfer,
  and receipt parsing. p95 for all attempts is also emitted, so failures are not
  hidden. No samples means null/N/A, not fabricated zero latency.
- Local launch/JIT/cold-connection effects can influence measurements; no
  production capacity inference is made.

Baseline capture paginates every stored event and requires a valid global chain.
After appends, every event is paginated again and ordered by sequence. Validation
checks duplicate EventId, SequenceNumber, and PreviousHash; sequence gaps;
genesis/predecessor relationships; unchanged baseline identities/hashes; exact
persisted/acknowledged run counts; and each successful receipt's EventId,
sequence, hash, and unique logical request token. External writes or receipt
ambiguity cause validation failure, not silent acceptance. Full verification
recomputes immutable stored hashes on the server; the harness does not try to
hash redacted API payloads.

Connection loss after commit can produce an unacknowledged persisted event.
The count/receipt checks detect that discrepancy; failed requests are not proof
of rolled-back transactions. This harness supplies no idempotency guarantee.
Baseline/final scans assume a quiet dedicated database, not an atomic snapshot
spanning the entire HTTP workload.

### Actual measured local run

Evidence: [machine-readable JSON](results/load-28d10bc7d6524f53a5b41b9dd396eaa7.json)
and [human-readable result](results/load-28d10bc7d6524f53a5b41b9dd396eaa7.md).

Run `28d10bc7d6524f53a5b41b9dd396eaa7` started at
`2026-10-03T08:01:44.7740264Z`. Environment: Windows, .NET 10 Debug build,
real local API `http://localhost:5115`, existing local PostgreSQL test database
`audit_log_test` on port 5433, and a 180-second HTTP timeout. The API was started
specifically for this run. No database reset or development-database writes were
performed; the baseline contained one valid event.

| Measurement | Actual result |
|---|---:|
| Parallel clients | 50 |
| Writes per client | 10 |
| Total append requests | 500 |
| Successful writes | 500 |
| Failed writes | 0 |
| Elapsed append workload | 14.940 seconds |
| Successful throughput | 33.467 writes/second |
| p95 successful append latency | 4415.896 ms |
| p95 all request latency | 4415.896 ms |
| Baseline / final event count | 1 / 501 |
| Final events verified | 501 |
| Duplicate ID/sequence/predecessor, gaps, link or receipt errors | None detected |
| Final global-chain status | Valid |
| Harness exit code | 0 |

Values above are rounded for display; the JSON retains measured precision and
all request samples. This is one local development observation with co-located
client/API/database and no warm-up. It is **not production performance**, an SLA,
a statistically representative benchmark, or evidence of horizontal scalability.

Harness validation: solution build succeeded; all 64 unit tests passed, including
two new tests for percentile/throughput calculations and absent measurements.
The recorded p95 was independently recalculated from its JSON samples. A separate
unavailable-API check produced explicit failed artifacts and exit code 1 without
inventing append measurements. The API process started for the measured run was
stopped afterward; the 500 synthetic events remain in the isolated test database.
