# Audit Log Service Assessment Plan

## Problem and approach

Build a runnable .NET / PostgreSQL prototype for the AI-assisted engineering assessment. The implementation will fully cover Scenarios A, B, C, and D. Scenarios E and F will receive deliberately bounded designs and documented implementation boundaries.

The service will use a single, globally ordered hash chain. PostgreSQL transactions and an advisory transaction lock will serialize append operations, so concurrent writers cannot fork the chain. Each immutable event stores a canonical-content SHA-256 hash and the preceding event hash; verification recomputes both values and reports the earliest violation.

For privacy-compatible redaction, sensitive payload fields will be represented at ingest as immutable cryptographic commitments, plus a separately stored redacted projection for reads. The original plaintext is neither retained nor exportable after its commitment is created. The chain hashes the immutable commitment-bearing canonical event, while clients receive the redacted projection. This protects sensitive values without mutating a chained event, with the explicit limitation that plaintext values cannot be restored or independently checked against a commitment without the original value and the applicable salt.

## Solution structure

- `AuditLogService.sln` — root solution.
- `src/AuditLogService.Api/` — ASP.NET Core Minimal API endpoints, request validation, error mapping, health endpoint, and OpenAPI.
- `src/AuditLogService.Application/` — append, query, verify, retention, redaction, export, and compliance-reporting use cases.
- `src/AuditLogService.Domain/` — immutable event model, canonical serialization, hashing, chain-verification result types, and validation invariants.
- `src/AuditLogService.Infrastructure/` — EF Core / Npgsql persistence, transactional append repository, PostgreSQL migrations, export packaging, and configuration.
- `tests/AuditLogService.UnitTests/` — canonicalization, hash-chain, query, commitment-redaction, and compliance-report tests.
- `tests/AuditLogService.IntegrationTests/` — API + PostgreSQL behavior, direct-store tamper detection, archive handling, exports, and recovery cases.
- `tests/AuditLogService.LoadTests/` — repeatable 50-client Scenario D load harness and result reporting.
- `docs/` — architecture, scenario evidence, requirement clarification, AI-usage traceability, operations/security boundaries, validation results, and final engineering summary.
- `docker-compose.yml` — reproducible PostgreSQL dependency for local development and test setup.
- Root documents — `README.md`, `ATTESTATION.md` template, and `.gitignore`.

## Delivery tasks

1. **Scaffold the solution and local environment**
   - Create a .NET 10 solution with API, domain, application, infrastructure, and test projects.
   - Configure code formatting, analyzers, health checks, OpenAPI, PostgreSQL connection configuration, Docker Compose, and developer setup instructions.
   - Define the attestation template without fabricating personal details.

2. **Model immutable events and tamper-evident persistence**
   - Define validated append requests, stored-event schema, canonical JSON serialization rules, SHA-256 hashing, genesis value, and database constraints/indexes.
   - Add transactional append logic: acquire a PostgreSQL transaction-scoped advisory lock, obtain the chain head, derive the predecessor hash and event hash, insert one event, and atomically update chain metadata.
   - Prevent application-level updates/deletes and use database privileges/triggers where practical to demonstrate immutability.

3. **Implement Scenario A APIs**
   - Add append, filtered/paginated query, and full-chain verification endpoints.
   - Support actor, resource type/id, event type, inclusive time range, stable cursor pagination, and validation errors.
   - Return verification status, first inconsistent event identifier, and a precise violation classification.
   - Add API and integration tests, including direct PostgreSQL mutation followed by verification failure.

4. **Implement Scenario B retention, redaction, and export**
   - Add configurable archive eligibility and a retention job/API that marks records archived without breaking order or altering hashed fields.
   - Transform configured sensitive JSON paths into salted cryptographic commitments for immutable storage and expose a redacted payload projection to callers.
   - Verify archived records from immutable chain data and distinguish archived from missing records.
   - Build actor/resource scoped exports containing selected events, chain context (boundary predecessor/successor), canonical-hash metadata, and an offline verification manifest.
   - Test retention behavior, redaction non-disclosure, commitment integrity, exports, and independent bundle verification.

5. **Clarify and implement Scenario C compliance reporting**
   - Write a requirements-clarification document identifying regulator jurisdiction, client definition, access-event taxonomy, report interval/time zone, identities, export format, authorization, retention, and evidentiary expectations as unresolved items.
   - State defensible prototype assumptions and scope: report access to `CLIENT_ACCOUNT` resources for a specified time range, with deterministic CSV/JSON output and chain-status metadata.
   - Implement a report endpoint/use case and cover its filters, time boundaries, empty reports, and excluded event types with tests.

6. **Implement and prove Scenario D concurrency correctness**
   - Document why a globally serialized transactional append path is selected over sharded chains: strongest ordering guarantee and simplest verification, at the cost of a single write bottleneck.
   - Add transaction rollback/retry boundaries and recoverable error handling so a failed append produces no partial event or head update.
   - Simulate failure during append and process restart behavior in integration tests.
   - Create a repeatable load harness with at least 50 parallel clients; verify no gaps, duplicate event identifiers, duplicate predecessors, or corrupted chain after execution; report total writes, throughput, and p95 append latency.

7. **Produce assessment evidence and bounded advanced designs**
   - Document Scenario E’s periodic verifier/checkpoint/metrics/runbook design and Scenario F’s per-tenant-chain authorization model as explicit future scope, without claiming implementation.
   - Write architecture, API contract, threat/risk/trade-off, validation, AI-use traceability, and final engineering-summary documents.
   - Record measured—not invented—load-test results and validation evidence.

8. **Validate the deliverable**
   - Run formatting/analyzers, unit tests, PostgreSQL integration tests, the load harness, and a local end-to-end API walkthrough.
   - Ensure the README lets the reviewer start the service, run tests, tamper with a controlled record in development, and observe a verification failure.

## Key decisions and trade-offs

- **Runtime:** .NET 10 LTS, ASP.NET Core Minimal API, EF Core/Npgsql, and PostgreSQL.
- **Hashing:** SHA-256 over a documented canonical UTF-8 JSON representation that includes immutable event fields, predecessor hash, event identifier, and assigned sequence number.
- **Timestamp:** server-assigned UTC timestamp; clients may provide an occurrence timestamp in the payload if needed, keeping audit acceptance time authoritative.
- **Concurrency:** a global PostgreSQL transaction-scoped advisory lock provides strict single-chain append correctness. It deliberately sacrifices horizontal write scalability; scaling options are documented, not prematurely implemented.
- **Retention:** archive state is stored outside hashed event content so eligible records retain all verification material. Physical deletion is deferred because it weakens full-chain verification and evidentiary value.
- **Redaction:** commitment-bearing immutable data plus redacted read projection avoids mutation of hashed content. This is a privacy-first prototype, but commitment salts and sensitive-path configuration require production-grade key/configuration governance before use.
- **Compliance:** the prototype documents ambiguities and implements only the explicitly assumed account-access report. Regulatory authentication, legal report certification, and jurisdiction-specific rules are excluded.
- **Scope:** A–D are implemented; E–F receive detailed designs and scope boundaries as allowed by the assessment guidance.

## Notes

- Personal attestation data will be left as user-supplied placeholders.
- Documentation will distinguish verified results from design proposals and will not fabricate load-test metrics or AI-use history.
- The implementation will use local configuration/secrets that are excluded from source control.
