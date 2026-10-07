# Testing and actual validation evidence

This document separates existing tests, previously recorded executions, and
commands to reproduce them. **No build, test suite, or load workload was rerun
for this documentation-only task.** Historical counts describe specific
delivery stages, not a new current-tree certification.

| Coverage boundary | Status |
|---|---|
| Unit/integration correctness tests and HTTP load harness | IMPLEMENTED |
| Scenario B export validation | PARTIALLY IMPLEMENTED: safe projection tested, not a verifiable bundle |
| E monitoring and F isolation/negative-test proposals | DESIGN ONLY: not executed tests |
| Production capacity certification, legal compliance, crash/failover proof | OUT OF SCOPE |

## Test infrastructure

The solution targets .NET 10; [global.json](../global.json) selects SDK 10.0.401
with feature-band roll-forward. Unit/integration projects use xUnit v2 and
Microsoft.NET.Test.Sdk/VSTest. Positional `dotnet test` commands below match that
platform. Build enables recommended analyzers and treats warnings as errors.

[PostgreSqlFixture](../tests/AuditLogService.IntegrationTests/Persistence/PostgreSqlFixture.cs)
applies migrations and resets event, projection, archive, and head data.
The shared xUnit database collection serializes fixture-dependent tests;
individual concurrency cases still run independent writer connections.

**Integration tests are destructive to their configured database.** Default is
the disposable `audit_log_test` service at localhost:5433.
`AUDITLOG_TEST_CONNECTION_STRING` must never target development/production data.
Do not run integration tests while the load harness uses the same database.
Privileged tampering/trigger tests require suitable test-only permissions.

## Existing test coverage

| Test source | Actual assertions/scenarios |
|---|---|
| [CanonicalEventSerializerTests](../tests/AuditLogService.UnitTests/CanonicalEventSerializerTests.cs) | Same canonical/hash output, mutation of all nine hash fields, deterministic genesis, fixed ordering, recursively sorted payloads/numbers, Unicode/null handling, duplicate-key rejection |
| [AuditEventValidationTests](../tests/AuditLogService.UnitTests/AuditEventValidationTests.cs) | Domain identifier, string, sequence, timestamp, payload, and hash invariants |
| [ChainVerifierTests](../tests/AuditLogService.UnitTests/Verification/ChainVerifierTests.cs) | Valid/empty chains and content, predecessor, genesis, duplicate/gap classification |
| [CommitmentPayloadProtectorTests](../tests/AuditLogService.UnitTests/Redaction/CommitmentPayloadProtectorTests.cs) | Exact/nested/array/escaped paths, fresh salts and digest construction, null/subtree/multiple values, safe projection and invalid policy/input |
| [AccountAccessReportTests (unit)](../tests/AuditLogService.UnitTests/Reporting/AccountAccessReportTests.cs) | Exact taxonomy, interval/resource guards, UTC and deterministic JSON behavior |
| [LoadTestResultTests](../tests/AuditLogService.UnitTests/LoadTesting/LoadTestResultTests.cs) | Measured-count/throughput/nearest-rank p95 calculations and null/N/A for unavailable measurements |
| [AuditEventPersistenceTests](../tests/AuditLogService.IntegrationTests/Persistence/AuditEventPersistenceTests.cs) | Persistence, duplicate EventId/sequence rejection, ordered retrieval |
| [AuditEventAppendTests](../tests/AuditLogService.IntegrationTests/Append/AuditEventAppendTests.cs) | Sequence/hash/predecessor correctness, transactional failures and concurrent append |
| [AuditEventApiTests](../tests/AuditLogService.IntegrationTests/Api/AuditEventApiTests.cs) | Append/query/verify contracts, filtering, cursors, validation and server-assigned fields |
| [AuditEventTamperingTests](../tests/AuditLogService.IntegrationTests/Api/AuditEventTamperingTests.cs) | Valid chain followed by privileged SQL content/link/sequence/deletion/head mutations; earliest inconsistency and classification |
| [AuditEventRetentionTests](../tests/AuditLogService.IntegrationTests/Retention/AuditEventRetentionTests.cs) | Strict cutoff/window, idempotent/concurrent archival, immutable fields/head unchanged, archived query and verification, missing/tampered archives |
| [AuditEventRedactionTests](../tests/AuditLogService.IntegrationTests/Redaction/AuditEventRedactionTests.cs) | Redacted POST/read/export projection, valid/tampered commitments, legacy masking, policy removal, projection mismatch, safe errors/logs and rollback |
| [AccountAccessReportTests (integration)](../tests/AuditLogService.IntegrationTests/Reporting/AccountAccessReportTests.cs) | Inclusive/equal/outside bounds, taxonomy exclusions, archive/redaction, deterministic bytes, empty/invalid requests, global corruption/missing rows, OpenAPI and concurrent snapshot |
| [ScenarioDCorrectnessTests](../tests/AuditLogService.IntegrationTests/Concurrency/ScenarioDCorrectnessTests.cs) | Three 50-writer repetitions, 150 writes each, observed advisory waiters, receipt/row reconciliation, full hashes/head; insert/head/precommit/connection failures, explicit retry and recreated API host |
| [HealthEndpointTests](../tests/AuditLogService.IntegrationTests/HealthEndpointTests.cs) | HTTP health endpoint behavior, not database readiness |

Redaction export tests test the **projection**, not a bulk endpoint or independent
bundle verifier. No tests establish implemented E monitoring or F isolation.

## Recorded results: historical, not rerun

Results below are recorded in the linked scenario evidence and prior delivery
validation. Counts increase as features were added; do not add stage totals
together.

| Delivery stage / source | Recorded actual outcome |
|---|---|
| [Scenario A tampering](scenarios/scenario-a.md#integration-test-evidence) | Build succeeded; 79 tests passed, 0 failed; 14 tampering cases |
| [B retention](scenarios/scenario-b-retention.md#migration-and-validation) | Build succeeded; 14 targeted retention cases; 93 full-suite tests passed, 0 failed; no pending EF model changes |
| [B redaction](scenarios/scenario-b-redaction.md#test-evidence) | Build succeeded; 127 tests passed (54 unit, 73 integration), no failures/skips; no pending EF model changes |
| [Scenario C](scenarios/scenario-c.md#tests-and-validation) | Build succeeded; 146 full-suite tests passed, 0 failed; added 8 unit and 11 integration cases |
| [D correctness](scenarios/scenario-d.md#running-and-interpreting-evidence) | Build succeeded; all 9 D cases passed; complete runner passed 155 tests, 0 failed |
| [D harness](scenarios/scenario-d.md#actual-measured-local-run) | Build succeeded; 64 unit tests passed, including 2 new metric tests; actual HTTP load run passed |

The latest recorded **full-suite** result is 155 tests, before the two
load-metric unit tests were added. The later **unit-only** result is 64.
There is no recorded post-harness full-suite execution to claim here. Historical
runner output/TRX is not packaged for every stage; the scenario documents are
the recorded summaries. The load run has raw persisted artifacts.

Earlier failure evidence is not hidden: the first tampering run exposed four
verification failures, including deleted-tail/all-chain false-valid results;
verification was corrected. D initially hit lock-wait timeouts in two cases;
test-only deadlines were increased and run-specific pools cleaned up before
the passing run. See the scenario documents for details.

## Actual real API/PostgreSQL load evidence

Run `28d10bc7d6524f53a5b41b9dd396eaa7` started
`2026-10-03T08:01:44.7740264Z` against local API `http://localhost:5115`,
PostgreSQL `audit_log_test` on port 5433, Windows/.NET 10 Debug configuration.

| Measurement | Actual result |
|---|---:|
| Parallel clients / writes each | 50 / 10 |
| Planned / completed requests | 500 / 500 |
| Successful / failed writes | 500 / 0 |
| Elapsed append workload | 14.9398915 seconds |
| Throughput | 33.46744519530145 successful writes/second |
| p95 successful / all-attempt latency | 4415.8961 / 4415.8961 ms |
| Baseline / final rows | 1 / 501 |
| Final verified events | 501 |
| Duplicate ID/sequence/predecessor, gaps, link/receipt errors | None detected |
| Full chain / harness outcome | Valid / passed, exit 0 |

Evidence: [raw JSON](scenarios/results/load-28d10bc7d6524f53a5b41b9dd396eaa7.json)
and [Markdown summary](scenarios/results/load-28d10bc7d6524f53a5b41b9dd396eaa7.md).
Elapsed time excludes baseline/final reads and verification. p95 is nearest-rank
over observed HTTP durations including receipt parsing. There were no retries.
No development database reset occurred; 500 synthetic events remained in the
selected test database after this run. Subsequent fixture runs can reset it.

This is one local observation, not production capacity, an SLA, or horizontal
scalability evidence. Unavailable-API harness behavior was also checked during
delivery: explicit failure artifacts/exit 1, not invented successful metrics.

## Reproduce

Prepare the dedicated PostgreSQL service using [README](../README.md).
From the repository root:

```powershell
dotnet build AuditLogService.sln
dotnet test tests\AuditLogService.UnitTests\AuditLogService.UnitTests.csproj --no-build
dotnet test tests\AuditLogService.IntegrationTests\AuditLogService.IntegrationTests.csproj --no-build
```

Or run the combined VSTest suite:

```powershell
dotnet test AuditLogService.sln --no-build
```

Useful targeted execution:

```powershell
dotnet test tests\AuditLogService.IntegrationTests\AuditLogService.IntegrationTests.csproj --filter FullyQualifiedName~ScenarioDCorrectnessTests
dotnet test AuditLogService.sln --filter FullyQualifiedName~Redaction
```

The LoadTests console project is not an xUnit test project and is not run by
`dotnet test`. Follow [Scenario D exact startup/migration instructions](scenarios/scenario-d.md#execute-exactly),
then run:

```powershell
dotnet run --no-build --project tests\AuditLogService.LoadTests -- http://localhost:5115 50 10 180 docs\scenarios\results
```

This command creates new measurements; the values above are not expected
thresholds. The installed EF CLI 8/runtime 10 mismatch was recorded previously;
use Infrastructure as both migration and startup project, with explicit matching
runtime/design-time connection settings. No tool upgrade is claimed.

## Limits of evidence

No coverage percentage, security penetration-test result, production benchmark,
cross-machine deployment proof, regulatory compliance, or zero-side-channel
guarantee is established. Host recreation is orderly WebApplicationFactory
recreation, not an OS crash. Connection failure uses a nonexistent database,
not a network partition. Ambiguous COMMIT acknowledgment, power loss,
replication failover, backups, and external telemetry confidentiality are not
proven by this suite. Future E/F negative tests are proposals only.
