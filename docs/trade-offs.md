# Engineering trade-offs and explicit boundaries

These decisions describe current code, not promises of production readiness.
See [architecture](architecture.md), [validation evidence](testing.md), and
[delivery reconciliation](final-engineering-summary.md).

## Global chain: IMPLEMENTED

**Choice:** one sequence space, one head, one predecessor chain for all events.

**Rationale:** strongest/simple global ordering and a straightforward full-chain
walk. A single order makes missing positions and predecessor inconsistencies
easy to explain and test.

**Cost:** every append shares a bottleneck; unrelated actors/resources cannot
write independent chains. Global verification/report metadata crosses all
resources. This architecture is not tenant isolation and cannot yield an
independent tenant-only history merely by filtering rows.

**Boundary:** sequence order is authoritative acceptance order, not necessarily
business occurrence order. Coherent privileged database rewrites still need an
independent trust anchor to detect.

## Transaction-scoped advisory lock: IMPLEMENTED

**Choice:** `pg_advisory_xact_lock(713204890501)` inside each append transaction.

**Rationale:** coordination occurs in the database across participating app
instances, not in one process. Lock, head read, insert/projection, and head update
are covered by transaction lifetime; commit/rollback releases the lock.

**Cost:** queued writers hold connections and can time out. Additional instances
increase waiting capacity, not the single serialized write service rate.
Horizontal write scalability is intentionally sacrificed.

**Boundary:** all writers must follow this cooperative protocol. Database unique
keys/append-only triggers supplement it, but privileged SQL is not constrained
by the application protocol. Disconnect cleanup depends on failure detection.
No queue, sharding, distributed service, or automatic retry is delivered.

## Atomicity versus exactly-once delivery

**IMPLEMENTED:** event/projection/head commit together; explicit known-rollback
retry and persisted-head recovery are tested.

**OUT OF SCOPE:** exactly-once logical requests and idempotency keys. A lost
COMMIT acknowledgment may hide a successful commit. Repeating the request can
produce another valid event; rollback correctness does not remove this ambiguity.

## Canonicalization and PostgreSQL precision: IMPLEMENTED

**Choice:** dedicated canonical JSON serializer and SHA-256 over exact UTF-8
event bytes, with payload sorting/exact number normalization and server UTC
timestamps truncated to microseconds.

**Rationale:** avoid unspecified serializer/property/number behavior and
PostgreSQL timestamp round-trip changes.

**Cost:** a custom version-sensitive format requires compatibility discipline
and independent verifier implementation. Scientific payload numbers may surprise
consumers that depend on original numeric spelling. Existing hashes must not be
silently recomputed under a changed format.

**Boundary:** read projections and ordinary API JSON are not canonical event
bytes. Determinism of a stored committed event does not mean two fresh ingests
with independently random salts have identical hashes.

## Retention choice: IMPLEMENTED service, not automation

**Choice:** logical archival in a separate metadata table; default window 90 days;
strict age eligibility. No physical deletion or cold-storage move.

**Rationale:** lifecycle changes do not mutate hashed content or discard
verification material. Atomic conflict handling makes repeated archival safe.

**Cost:** storage grows indefinitely; archived rows remain in reads/reports.
Archive status is not authenticated by the event hash and is not privacy erasure.

**Boundary:** an operator/service caller must invoke the retention service.
A scheduler/archive HTTP API and legal deletion policy are OUT OF SCOPE.
The prototype window is not a jurisdiction-specific retention rule.

## Redaction commitments: IMPLEMENTED within configured paths

**Choice:** replace selected values at ingest with SHA-256 commitments using
fresh public salts; hash that immutable representation and expose separately
masked projections.

**Rationale:** preserves chain evidence without retaining new configured
sensitive plaintext or rewriting historical hashes.

**Cost:** original values cannot be restored. Public salts reduce correlation
and precomputation but do not prevent per-record guessing of low-entropy values.
Unconfigured copies, names, and event identifiers are unprotected.

**Boundary:** this is not encryption or blanket sanitization. Legacy plaintext
in database/backups remains. Application-buffer clearing is not complete memory
erasure. A future keyed scheme needs key management, versioning, rotation, and
migration decisions; it is not implicitly present.

## Export: PARTIALLY IMPLEMENTED

**Choice delivered:** a redaction-safe event projection helper.

**Unfulfilled assessment requirement:** actor/resource-scoped bulk endpoint,
self-contained chain context/manifest, and independent bundle verification.
Normal query/report JSON is not a substitute: masked payloads cannot reproduce
ContentHash.

**Rationale/boundary:** staged requests delivered redaction and safe projection,
but did not deliver the planned packaging operation. This remains an assessment
gap, not a completed feature or a justified claim of full Scenario B coverage.

## Compliance scope: IMPLEMENTED normalized prototype

**Choice:** deterministic JSON for exact CLIENT_ACCOUNT resources, three explicit
access event types, inclusive UTC acceptance interval, sequence ordering, and
global snapshot chain status.

**Rationale:** normalize an ambiguous regulator request without inventing legal
obligations. One repeatable-read snapshot avoids mixing report rows and integrity
metadata from different points in time.

**Cost:** full global verification and matching-row materialization are expensive;
new unrelated events can change report metadata. Producer actor/outcome assertions
are not identity or completeness proof.

**Boundary:** CSV was not included in the selected "CSV or JSON" design.
Certification, jurisdictional rules, regulator authentication, approval workflow,
and lawful-access conclusions are OUT OF SCOPE.

## Operational security and resource bounds

**IMPLEMENTED:** bounded query pages, parameterized database operations,
generic validation/error responses, fixed payload-rejection logs, no request-body
or EF sensitive-data logging enabled by application code.

**PARTIALLY IMPLEMENTED:** deployment safety. Auth/rate limiting, least-privilege
role provisioning, and bounded full verification/reports are not delivered.
Credential-bearing local defaults are not safe deployment configuration.
Do not interpret absence of an observed leak/injection as a comprehensive
security certification.

## Scenario E: DESIGN ONLY

[Scenario E](scenarios/scenario-e.md) proposes scheduling, lag metrics,
checkpoints/signing/anchoring, alerts, evidence preservation, scaling, and a
runbook. None is implemented. Manual full-chain verification is not scheduled
monitoring or anchored history.

Suffix verification alone cannot detect later changes in an already verified
prefix; the proposed design therefore separates recent coverage from historical
rescans. Proposed thresholds are not measured SLOs.

## Scenario F: DESIGN ONLY

[Scenario F](scenarios/scenario-f.md) proposes authenticated tenant identity,
default-deny action/tenant authorization, per-tenant chains, scoped query/verify/
export, leakage defenses, and migration.

No tenant identity, authorization, RLS policy, per-tenant head/lock, or negative
isolation test is implemented. Moving to per-tenant chains would reduce coupling
and blast radius but sacrifice current global order and require a versioned
cutover preserving legacy evidence. Filtering the current global chain does not
implement this design.
