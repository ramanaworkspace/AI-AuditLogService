# AI-assisted engineering evidence

Evidence snapshot: 2026-10-07. This record is limited to the repository,
available Git history, development-session checkpoints, and the interaction
history available for this work. It is not a complete transcript or a
line-by-line attribution of authorship.

The engineer reviewed and validated AI-assisted output. Evidence includes
iterative acceptance criteria, examination of implementation and test results,
follow-up defect reports, bounded-scope instructions, and requests for actual
build/test validation. The engineer remains responsible for reviewing, adapting,
and defending the submitted result.

## Evidence sources and limits

- [GitHub Copilot prompt library](../GitHub-Copilot-Audit-Log-Service-Prompts.md)
  contains staged prompts and an execution-order checklist. Prompt 0 is marked
  done; the checklist for later prompts remains unchecked. Its contents alone
  do not prove that each prompt was run.
- Git history contains commits for scaffolding, domain/hashing, persistence,
  append, Scenario A APIs and tampering, retention/redaction, Scenario C, and
  Scenario D. The commit titled “implemented redaction and bulk export” must
  not be treated as evidence of the assessment's verifiable bundle: the current
  source and documentation contain a safe projection helper, but no bulk
  export endpoint, manifest, boundary package, or independent verifier.
- Session checkpoints retain summaries of several implementation/debugging
  turns. They are summaries, not full prompts or complete transcripts.
- Exact per-turn AI outputs, reviewer comments, and a file-by-file
  accept/modify/reject ledger are **Not captured** unless specifically evidenced
  below.

## 1. Requirements

**Task.** Interpret the assessment and `plan.md`, implement only the requested
delivery stage at each step, and preserve the stated scenario boundaries.

**AI assistance and prompt purpose.** The available development history includes
requirements review and successive implementation requests for the event
domain, canonical hashing, PostgreSQL append, Scenario A APIs/tampering,
Scenario B retention/redaction, Scenario C clarification/reporting, Scenario D
correctness/load testing, E/F design-only documentation, and test-gap analysis.
The prompt library groups these tasks in execution order. Exact wording and
responses for every historical turn are **Not captured**.

**Relevant output accepted.** The repository contains the planned .NET solution
and scenario artifacts. The Scenario C clarification document explicitly
separates the original statement, unresolved questions, prototype assumptions,
normalized requirement, and scope. E/F documents explicitly say
`IMPLEMENTED: NO` and `DESIGN ONLY`.

**Modified or rejected.** Follow-up work corrected or constrained earlier
output when it contradicted observed behavior or the requested scope (see
Debugging and Scope Management below). No complete transcript establishes
which initial requirement summaries or alternative interpretations were
accepted or rejected individually: **Not captured**.

**Validation.** Requirement evidence is in
[the assessment](../audit-log-service-assessment-requirements.md),
[the plan](../plan.md), and
[the Scenario C clarification](scenarios/scenario-c-requirement-clarification.md).
Tests and validation records are summarized in
[testing documentation](testing.md); historical test runs are not represented
as rerun for this document.

## 2. Architecture

**Task.** Choose and implement an auditable chain, persistence, and concurrency
model within the existing scaffold.

**AI assistance and prompt purpose.** The staged prompts asked for an immutable
event model, deterministic canonical JSON, SHA-256, a defined genesis value,
PostgreSQL persistence, and transactional append under a PostgreSQL
transaction-scoped advisory lock. The exact design discussion/transcript is
**Not captured**.

**Relevant output accepted.** The current code and documentation use one
global sequence/head, `GENESIS`, canonical UTF-8 serialization, SHA-256,
PostgreSQL transactions, and a fixed transaction-scoped advisory lock.
[Architecture](architecture.md),
[canonical hashing](canonical-hashing.md), and
[Scenario D](scenarios/scenario-d.md) describe these choices and trade-offs.

**Modified.** Development checkpoints document a real persistence/hash
precision issue: PostgreSQL timestamps have microsecond precision while .NET
timestamps can carry an additional tick. The append path was changed to
truncate timestamps before hashing, and regression validation was requested.
The checkpoints also record using isolated test-database configuration and
test-only trigger bypasses for privileged tampering cases. These changes
preserved the selected architecture.

**Rejected or deferred.** E background integrity monitoring and F tenant
authorization/per-tenant chains were explicitly constrained to design-only
work. They are proposals, not delivered architecture. The bulk-export design
was part of the plan and prompt library, but the present implementation stops
at a redaction-safe projector. The reason the full package was not completed
is **Not captured**; its absence is acknowledged as an assessment gap.

**Validation.** Canonicalization, field sensitivity, genesis, verification,
database persistence, and append invariants have unit/integration coverage.
Historical run evidence and limitations are in [testing](testing.md) and
[Scenario D](scenarios/scenario-d.md). No external independent chain anchor
exists.

## 3. Implementation

**Task.** Build the requested service stages by reusing the scaffolded
projects, without duplicating the solution or crossing explicit scenario
boundaries.

**AI assistance and prompt purpose.** The prompt library includes the staged
implementation requests; Git commits provide corroborating artifact-level
history. The current solution contains API, Application, Domain,
Infrastructure, UnitTests, IntegrationTests, and LoadTests projects.

**Relevant output accepted.** Current implementation artifacts include:

- Scenario A append, filtered query, pagination, chain verification, and
  direct-store tampering detection.
- Scenario B retention state separated from hashed event data and commitment
  redaction for configured payload paths.
- Scenario C's bounded account-access report.
- Scenario D concurrency correctness tests and a real HTTP load-test harness.

**Modified or rejected.** The engineer's staged requests explicitly excluded
unrequested functionality at each task boundary. E/F implementation was
withheld as directed. For export, the project history contains a commit title
mentioning bulk export, but current code/docs show that only a projection helper
was delivered; no evidence explains that difference beyond the documented
partial boundary. Per-file AI-versus-engineer authorship and the detailed
disposition of generated code are **Not captured**.

**Validation.** Delivery commits and corresponding source/tests are listed in
[the final engineering summary](final-engineering-summary.md). Current
implementation limitations are documented in
[architecture](architecture.md) and
[trade-offs](trade-offs.md). Commit titles are not treated as proof that every
planned feature exists.

## 4. Testing

**Task.** Validate hash-chain behavior, API contracts, PostgreSQL integration,
concurrency, reporting, redaction, and load-harness measurements.

**AI assistance and prompt purpose.** The available prompts requested unit,
integration, concurrency, tampering, and load tests, then a test-gap analysis
and targeted missing tests. Exact historical test-generation conversations
are **Not captured**.

**Relevant output accepted.** The source includes unit tests for canonical
serialization, hashing, validation, verification, redaction, reporting, and
load metrics; PostgreSQL integration tests cover APIs, mutation, retention,
redaction, reporting, and Scenario D; the load harness records actual request
outcomes, latency, throughput, and chain checks.

**Modified.** The test-gap review found that the API test named for actor,
resource, and event-type filtering only asserted actor filtering. The test was
expanded to exercise each supported filter separately. A keyset pagination test
was also added for matching records appended between page requests. No
production code was changed for those tests.

**Rejected or not completed.** No tests were invented for a full export bundle
or independent verifier because those production surfaces are absent. New
PostgreSQL-dependent query/pagination tests could not run in the latest
available validation because `AUDITLOG_TEST_CONNECTION_STRING` was not set.

**Validation performed, with result scope stated explicitly.**

- The updated integration-test project built successfully with zero warnings
  and zero errors.
- The unit test project passed 64 tests.
- The targeted new PostgreSQL integration cases did not reach their assertions:
  fixture construction failed because the required disposable test-database
  connection setting was absent.
- The latest full solution test command likewise had 96 integration cases fail
  during fixture setup for that missing setting; 1 integration case passed and
  64 unit tests passed. These are not successful integration-suite results.
- Earlier successful full-suite and load-test results are historical records in
  [testing documentation](testing.md), not runs performed for this evidence
  document. The recorded load run is local-only and is not production
  performance evidence.

The engineer reviewed those outputs and did not claim that database-backed
tests passed when setup prevented them from running.

## 5. Debugging

**Task.** Investigate failures using observed test/build/database behavior and
repair only supported defects.

**AI assistance and prompt purpose.** Session checkpoints describe iterative
debugging requests for connection configuration, test isolation, sequence
behavior, PostgreSQL timestamp precision, tampering, and redaction tests.
Exact prompts and full responses are **Not captured**.

**Relevant output accepted and modified.**

- The integration fixture's connection configuration was corrected so tests
  use the configured dedicated database rather than a development database.
- A reproducible intact-chain verification failure was traced to PostgreSQL
  timestamp precision and addressed by truncating the timestamp before hashing.
- A tampering test was adapted to bypass the append-only trigger only for the
  intentional privileged test mutation, rather than weakening production
  immutability.
- Subsequent redaction work added safe generic errors/logging behavior and
  regression tests for tested sensitive-value leak paths.

These changes are recorded in session checkpoints and reflected in source,
tests, and scenario documents. Exact AI-generated patch text and
line-by-line acceptance records are **Not captured**.

**Rejected.** No evidence indicates that a production append-only trigger was
removed to make tampering tests pass; the recorded approach preserved it and
changed test setup instead.

**Validation.** Earlier checkpoints and scenario documents record successful
build/test stages after fixes. The newest query/pagination integration tests
remain unvalidated against PostgreSQL until a disposable test connection is
provided.

## 6. Documentation

**Task.** Document implemented behavior, assumptions, validation, trade-offs,
and explicit boundaries without fabricating evidence.

**AI assistance and prompt purpose.** Prompts requested scenario documentation,
API/architecture/testing/trade-off/final-summary documents, then AI-use
traceability. Exact generation transcripts are **Not captured**.

**Relevant output accepted.** The repository contains README setup guidance,
canonical-hashing documentation, architecture/API/testing/trade-off/final
summary documents, and Scenario A-F documents.

**Modified or rejected.** Documentation distinguishes implemented,
partially implemented, design-only, and out-of-scope work. It does not claim
that E/F features exist, that the report is regulatory certification, or that
the chain makes tampering impossible. A mismatch remains between
`plan.md`/prompt-library export intent and the present implementation. This
document records the discrepancy rather than recharacterizing the projector
as a verifiable export.

**Validation.** Historical documentation checks are recorded in the session
history. The current AI-usage document was compared with the available prompt
library, commits, checkpoints, test sources, and recorded validation outcomes.

## 7. Security review

**Task.** Review the implementation for security, privacy, and reliability
risks without inventing vulnerabilities or making code changes during the
review.

**AI assistance and prompt purpose.** A read-only senior architecture/security
review was requested. The available development summary records findings
concerning missing authentication/authorization, resource-exhaustion exposure
from full-history operations, public-salt guessing risk, configured-path-only
redaction, incomplete export, and lack of an independent chain anchor. The
verbatim review output is **Not captured** in the repository.

**Relevant output accepted.** Current architecture/trade-off documentation
records the material boundaries: the prototype has no authentication,
authorization, tenant isolation, or rate limiter; public salts do not prevent
guessing of low-entropy values; full-history verification/report operations
are not bounded; and Scenario B verifiable export and Scenario E anchoring are
absent.

**Modified or rejected.** A later repository-validation pass removed
credential-bearing connection defaults, changed local database bindings to
loopback, updated README configuration guidance, and restored placeholders in
the attestation rather than retaining personal identity values. These changes
are present in the current worktree and should not be confused with a fix for
the remaining authentication/authorization or workload-bounding gaps. The
review's full finding-by-finding disposition is **Not captured** as a
committed security report.

**Validation.** A complete, reproducible security-review report is not
persisted here. Current security boundaries are documented in
[architecture](architecture.md), [trade-offs](trade-offs.md), and the
scenario documents. The engineer must independently review and validate
security-sensitive changes and complete the personal attestation before
submission; this document is not that attestation.
