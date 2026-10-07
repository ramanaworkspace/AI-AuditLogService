# Audit Log Service

## Prerequisites

- .NET SDK 10.0.401 or later in the 10.0 feature band
- Docker Desktop or another Docker-compatible Compose runtime

## Start PostgreSQL

Supply passwords from an external secret source or an ignored `.env` file.
Do not commit them. For an interactive PowerShell session:

```powershell
$devSecret = Read-Host 'Local development database password' -AsSecureString
$testSecret = Read-Host 'Disposable test database password' -AsSecureString
$env:AUDITLOG_POSTGRES_PASSWORD = [System.Net.NetworkCredential]::new('', $devSecret).Password
$env:AUDITLOG_TEST_POSTGRES_PASSWORD = [System.Net.NetworkCredential]::new('', $testSecret).Password
docker compose up -d postgres
```

The Compose development database is `audit_log`, user `audit_log`, bound to
`127.0.0.1:5432`. If port 5432 already belongs to another local database, do not
stop or overwrite it: use your intended server/database with explicit connection
configuration instead. Changing an environment password does not rotate an
existing PostgreSQL volume's credentials.

Runtime and migration credentials are required externally:

```powershell
$env:ConnectionStrings__AuditLogDatabase = "Host=localhost;Port=5432;Database=audit_log;Username=audit_log;Password=$env:AUDITLOG_POSTGRES_PASSWORD"
$env:AUDITLOG_CONNECTION_STRING = $env:ConnectionStrings__AuditLogDatabase
dotnet ef database update --project src\AuditLogService.Infrastructure --startup-project src\AuditLogService.Infrastructure
```

The simple connection-string example assumes a password without connection-string
delimiter characters; otherwise provide a correctly escaped full connection
string using an Npgsql connection-string builder or your secret manager.
The API does not apply migrations automatically. No password or fallback
database connection is stored in application settings or the design-time factory.

## Run PostgreSQL persistence integration tests

```powershell
docker compose up -d postgres-test
$env:AUDITLOG_TEST_CONNECTION_STRING = "Host=localhost;Port=5433;Database=audit_log_test;Username=audit_log;Password=$env:AUDITLOG_TEST_POSTGRES_PASSWORD"
dotnet test tests\AuditLogService.IntegrationTests
```

The test service uses a separate ephemeral database bound to `127.0.0.1:5433`;
its container data is not persisted. `AUDITLOG_TEST_CONNECTION_STRING` is required
and must target a disposable dedicated database: the fixture truncates its data.
Do not run integration tests concurrently with the load harness on that database.

## Restore, build, and test

```powershell
dotnet restore AuditLogService.sln
dotnet build AuditLogService.sln --no-restore
dotnet test AuditLogService.sln --no-build
```

## Run the API

```powershell
dotnet run --project src\AuditLogService.Api
```

The service exposes the Scenario A endpoints:

- `POST /api/v1/audit-events`
- `GET /api/v1/audit-events`
- `GET /api/v1/audit-events/verify`

See [Scenario A](docs/scenarios/scenario-a.md) for the API contract, tampering
validation, violation classifications, and limitations.

Scenario B retention is implemented as an explicit service operation. Archive
metadata is separate from hashed events; query responses expose archive status
and verification still checks all records. See
[retention](docs/scenarios/scenario-b-retention.md) for configuration, migration,
service invocation, and scope boundaries.

Scenario B structured redaction replaces configured sensitive payload values
with immutable salted commitments before hashing. Reads and the export projection
mask them without changing chain evidence. See
[redaction](docs/scenarios/scenario-b-redaction.md) for JSON pointer configuration,
the additive read-projection migration, test coverage, public-salt limitations,
and the boundary between safe export projection and future verifiable bundles.
Apply pending EF migrations before starting the updated API; integration tests
apply them automatically only to the dedicated test database.

Scenario C exposes `GET /api/v1/reports/account-access` with explicit inclusive
start/end times, a fixed account-access event allowlist, deterministic redacted
JSON, and snapshot global-chain status. See [Scenario C](docs/scenarios/scenario-c.md)
for the taxonomy, schema, validation, and non-regulatory scope.

Scenario D adds repeatable 50-writer PostgreSQL contention and failure/recovery
correctness tests without changing the global chain architecture. See
[Scenario D](docs/scenarios/scenario-d.md) for invariants, retry boundaries,
test commands, and the distinction from performance benchmarking.
The [load-test harness](tests/AuditLogService.LoadTests/) also measures real HTTP
appends and saves JSON/Markdown evidence; [Scenario D](docs/scenarios/scenario-d.md)
contains exact execution commands and the measured local result.

Infrastructure endpoints:

- `GET /health`
- `GET /openapi/v1.json` when running in the Development environment

## Delivery boundaries and final evidence

Scenario B bulk verifiable export remains **PARTIALLY IMPLEMENTED**: the safe
projection exists, but there is no bulk endpoint or independently verifiable
bundle. E/F are **DESIGN ONLY**. Authentication, tenant isolation, and production
hardening must not be inferred from this prototype.

See [architecture](docs/architecture.md), [API contract](docs/api.md),
[testing](docs/testing.md), [trade-offs](docs/trade-offs.md), and
[engineering summary](docs/final-engineering-summary.md).
