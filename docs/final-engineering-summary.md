# Final engineering summary

Snapshot: 2026-10-03. This is an engineering account of existing code and
recorded evidence, not a claim of complete assessment delivery or production
readiness. Navigation: [architecture](architecture.md), [API](api.md),
[testing](testing.md), [trade-offs](trade-offs.md).

## Plan and rationale

[plan.md](../plan.md) selected .NET 10 Minimal APIs, EF Core/Npgsql/PostgreSQL,
one canonical SHA-256 chain, and transaction-scoped advisory locking. The intent
was complete A-D coverage and bounded E/F designs. Incremental implementation
reused the scaffolded projects rather than duplicating the solution.

The core rationale is correctness first: one durable head and serialized atomic
append simplify global ordering, predecessor allocation, rollback, and
verification. Separate lifecycle/read metadata allows archive/redaction behavior
without changing hashed event content. Scenario C was normalized before code.

**Reconciliation with the plan:** its statements that A-D are complete and that
export packaging exists are broader than the delivered code. Scenario B bulk
verifiable export remains missing. Retention is an explicit service, not an
automated job/API. Shared local configuration also contains credential-bearing
defaults despite the plan's aspiration to exclude secrets. This summary records
those discrepancies; it does not modify code or retroactively declare them done.

## Delivered scenarios and status

| Area | Status | Actual artifact/boundary |
|---|---|---|
| Foundation and Scenario A | IMPLEMENTED | Immutable validated events, custom canonical UTF-8/SHA-256, GENESIS, migrations/constraints, transactional append, filtered keyset query, full verification and privileged tampering tests |
| B.1 retention | IMPLEMENTED | Configurable eligibility and idempotent archive service, separate metadata, retained verification material; no scheduler/HTTP archive operation |
| B.2 structured redaction | IMPLEMENTED | Exact configured JSON pointers, fresh public-salt commitments at ingest, masked read/report/export projections, no restoration of newly protected values |
| B.3 bulk export | PARTIALLY IMPLEMENTED | Safe projector only; no required endpoint, self-contained bundle, boundary context/manifest, or independent verifier |
| Scenario B overall | PARTIALLY IMPLEMENTED | Required retention/redaction delivered; required export gap remains |
| Scenario C | IMPLEMENTED | Clarification and normalized CLIENT_ACCOUNT access report, exact success/denied/failed allowlist, deterministic JSON, inclusive interval, global snapshot chain metadata |
| Scenario D | IMPLEMENTED | Repeatable 50-writer correctness/failure cases and finite real HTTP load harness with persisted actual measurements |
| Scenario E | DESIGN ONLY | Background verification, scheduling/lag, independent checkpoints, metrics/alerts/runbook and scaling proposals |
| Scenario F | DESIGN ONLY | Future tenant identity/authorization, per-tenant chains and scoped surfaces, leakage/negative-test and migration proposals |
| Physical deletion/cold storage, CSV report, legal certification, exactly-once requests | OUT OF SCOPE | No current implementation or guarantee |

The assessment allows E/F designs when an advanced scenario is attempted in
full; D is the implemented advanced scenario. That does not waive required B.3.

## Architecture and artifacts

The existing [solution](../AuditLogService.sln) contains API, Application, Domain,
Infrastructure, UnitTests, IntegrationTests, and LoadTests projects.
PostgreSQL stores JSONB events and projection payloads, one global head, and
separate archive markers. Three migrations cover initial persistence, archives,
and read projections.

An append protects payloads, acquires the fixed PostgreSQL transaction-scoped
advisory lock, reads/advances the head, inserts event/projection, and commits
atomically. Server-assigned UTC time is microsecond-aligned before hashing.
Verification compares sequence/link/content and retained head evidence in one
repeatable-read snapshot. No HTTP update/delete operation exists.

Detailed artifacts:

- [Canonical hash specification](canonical-hashing.md) and [concurrency](concurrency.md).
- [A tampering](scenarios/scenario-a.md), [B retention](scenarios/scenario-b-retention.md),
  [B redaction](scenarios/scenario-b-redaction.md).
- [C clarification](scenarios/scenario-c-requirement-clarification.md) and
  [C report](scenarios/scenario-c.md).
- [D correctness/load evidence](scenarios/scenario-d.md).
- [E DESIGN ONLY](scenarios/scenario-e.md) and [F DESIGN ONLY](scenarios/scenario-f.md).
- [Raw local load evidence](scenarios/results/load-28d10bc7d6524f53a5b41b9dd396eaa7.json)
  and [readable result](scenarios/results/load-28d10bc7d6524f53a5b41b9dd396eaa7.md).

## Validation: actual recorded results

The most recent recorded complete regression run passed **155 tests, zero
failures**, after Scenario D correctness additions. The later load-harness
delivery passed **64 unit tests**, including two new metric tests, and built
successfully. A post-harness full-suite result is **not recorded** and is not
invented here. Stage history and commands are in [testing](testing.md).

The actual local HTTP/PostgreSQL run used 50 clients and 10 writes each:
500 successful writes, zero failed attempts, 14.9398915 seconds elapsed,
33.46744519530145 writes/second, and 4415.8961 ms nearest-rank p95.
The chain grew from 1 to 501 rows; final verification was valid with no detected
duplicate IDs/sequences/predecessors, gaps, link errors, or receipt mismatches.
These are one local Debug-build observation, not production performance.

Correctness tests cover insert/head/precommit failures, rollback, explicit retry
after known failure, nonexistent-database connection failure, and orderly
recreated-host recovery. These are not crash/partition/failover proofs.

This final-documentation task did not rerun builds/tests/load or mutate a
database. Documentation links/content and whitespace are checked separately;
historical results are explicitly labeled.

## Risks and assumptions

1. **Network exposure:** no authentication/authorization/rate limiter; actor
   identity is a producer assertion. Keep the prototype isolated.
2. **Database trust:** local credentials/default administrative identity are
   unsuitable for deployment. External secrets and separate least-privilege
   runtime/migration roles remain necessary.
3. **Integrity trust:** privileged coherent chain/head replacement can evade
   internal verification without independently anchored evidence. E is not
   implemented.
4. **Privacy:** public salts permit guessing low-entropy values. Only configured
   paths are protected; legacy plaintext, unconfigured copies/identifiers,
   external logs/backups and transient memory remain separate concerns.
5. **Availability:** one serialized write path, unbounded retained history,
   and full-history report/verification work limit capacity. Query-page limits
   do not bound all endpoints.
6. **Delivery semantics:** no automatic retry/idempotency. Uncertain COMMIT
   acknowledgment can lead to duplicate logical events on retry.
7. **Export gap:** masked API/report content is not offline-verifiable evidence.
8. **Configuration/tooling:** runtime/design-time/test connection settings must
   target the intended database; integration fixtures reset data. The previously
   recorded EF CLI 8/runtime 10 warning is not a resolved tooling upgrade.

Assumptions: all participating writers follow the same global lock protocol;
PostgreSQL provides the transaction/durability behavior of its configured
deployment; SHA-256 collision resistance holds; server acceptance time is
authoritative; producers use the documented account-access taxonomy and supply
trustworthy assertions; redaction policy is governed before ingest. None proves
regulatory compliance or completeness of event capture.

## Limitations and completion boundary

The system is **tamper-evident, not tamper-proof**. Tampering with a chained
record can be detected when the affected record and subsequent chain
relationships are verified. Hashes do not supply confidentiality, authorization,
identity authenticity, non-repudiation, or legal certification.

Archive markers preserve evidence but do not reclaim storage or erase data.
Report JSON is deterministic for a declared snapshot/policy, not immutable
across appends or archival. Query cursors are sequence positions, not encrypted
tenant tokens. Health does not test PostgreSQL readiness.

No E/F capability or future negative test is claimed as implemented.
No new performance, security, coverage, AI-use, or personal attestation result
is invented. Required export work and production hardening remain visible gaps,
not silently completed tasks. This delivery stops at engineering documentation.
