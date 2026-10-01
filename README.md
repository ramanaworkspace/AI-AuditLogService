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

The initial scaffold exposes only infrastructure endpoints:

- `GET /health`
- `GET /openapi/v1.json` when running in the Development environment
