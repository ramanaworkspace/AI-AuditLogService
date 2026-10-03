# Audit Log Service

## Prerequisites

- .NET SDK 10.0.401 or later in the 10.0 feature band
- Docker Desktop or another Docker-compatible Compose runtime

## Start PostgreSQL

```powershell
docker compose up -d postgres
```

The development database is exposed on `localhost:5432`. Its local-only connection
settings are in `src/AuditLogService.Api/appsettings.Development.json`.

## Run PostgreSQL persistence integration tests

```powershell
docker compose up -d postgres-test
dotnet test tests/AuditLogService.IntegrationTests
```

The test service uses a separate ephemeral database on `localhost:5433`; its
container data is not persisted. Set `AUDITLOG_TEST_CONNECTION_STRING` to use a
different dedicated test database.

## Restore, build, and test

```powershell
dotnet restore AuditLogService.sln
dotnet build AuditLogService.sln --no-restore
dotnet test AuditLogService.sln --no-build
```

## Run the API

```powershell
dotnet run --project src/AuditLogService.Api
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

Infrastructure endpoints:

- `GET /health`
- `GET /openapi/v1.json` when running in the Development environment
