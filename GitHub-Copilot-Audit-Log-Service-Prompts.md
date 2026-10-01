# GitHub Copilot Execution Prompts — Audit Log Service Assessment

> Run these prompts sequentially in GitHub Copilot Agent mode. Prompt 0 has already been executed. Do not paste all prompts into Copilot at once. After each implementation prompt, review the changes, run the requested validation, and commit the work before moving to the next prompt.

## Execution Order

Prompt 0  — Requirements understanding — **Already completed**
Prompt 1  — Fresh .NET solution
Prompt 2  — Domain model + canonical hashing
Prompt 3  — PostgreSQL + EF Core persistence
Prompt 4  — Transactional append + PostgreSQL advisory lock
Prompt 5  — Scenario A APIs
Prompt 6  — Scenario A tampering validation
Prompt 7  — Scenario B retention
Prompt 8  — Scenario B structured redaction
Prompt 9  — Scenario B bulk export
Prompt 10 — Scenario C requirement clarification
Prompt 11 — Scenario C implementation
Prompt 12 — Scenario D concurrency testing
Prompt 13 — Scenario D load test
Prompt 14 — Scenario E design only
Prompt 15 — Scenario F design only
Prompt 16 — Security/reliability review
Prompt 17 — Final documentation
Prompt 18 — Final validation
Prompt 19 — Test gap analysis
Prompt 20 — Fix test gaps
Prompt 21 — AI usage traceability + final assessment review

---

# Prompt 0 — Understand Requirements

**Status: Already executed**

```text
You are assisting me as an AI engineering pair-programmer for this assessment.

Read these two files first and treat them as the authoritative requirements and implementation plan:

1. audit-log-service-assessment-requirements.md
2. plan.md

Do not start implementing yet.

First:
1. Summarize the assessment requirements.
2. Summarize the implementation plan.
3. Identify the required scenarios and their implementation boundaries.
4. Identify the important architectural decisions already made in plan.md.
5. Identify any contradictions or ambiguities between the requirements document and plan.md.
6. Do not silently change the plan.
7. If something requires a decision, present the decision to me before implementation.

Important assessment constraints:
- This is an individual engineering assessment.
- AI assistance is expected.
- The engineer owns all implementation decisions and validation.
- Do not fabricate test results, load-test results, AI usage history, personal information, or attestation information.
- Keep implementation traceable through small Git commits.
- Prefer simple, production-quality engineering over unnecessary technology.

Do not modify any files in this step.
```

---

# Prompt 1 — Create the Fresh Solution

```text
We are starting the implementation of the Audit Log Service assessment from a completely fresh repository.

Prompt 0 has already been completed.

Read these files before implementing:

- audit-log-service-assessment-requirements.md
- plan.md

Implement ONLY Delivery Task 1 from plan.md: Scaffold the solution and local environment.

Do not implement any business functionality yet.

Create this solution structure:

AuditLogService.sln

src/
  AuditLogService.Api/
  AuditLogService.Application/
  AuditLogService.Domain/
  AuditLogService.Infrastructure/

tests/
  AuditLogService.UnitTests/
  AuditLogService.IntegrationTests/
  AuditLogService.LoadTests/

docs/

Also create:

docker-compose.yml
README.md
ATTESTATION.md
.gitignore

Technology decisions from plan.md:

- .NET 10
- ASP.NET Core Minimal API
- Entity Framework Core
- Npgsql
- PostgreSQL
- xUnit

Configure the solution for:
- nullable reference types
- implicit usings
- analyzers/code quality
- consistent formatting
- OpenAPI
- health checks
- PostgreSQL configuration
- Docker Compose PostgreSQL
- unit testing
- integration testing
- load-test project

ATTESTATION.md must contain placeholders only.

Do NOT invent:
- my name
- my email
- dates
- assessment results
- AI usage history

Use placeholders such as:
<YOUR FULL NAME>
<YOUR EMAIL>
<START DATE>
<SUBMISSION DATE>

README.md should contain only initial setup information at this stage.

Important:
- Do not create unnecessary projects.
- Do not add authentication yet.
- Do not add business APIs yet.
- Do not add Kafka, Redis, AWS, Kubernetes, or other infrastructure not required by the plan.
- Keep the solution simple and aligned with plan.md.

After implementation:
1. Run dotnet restore.
2. Run dotnet build.
3. Run the available tests.
4. Fix actual build/configuration problems.
5. Do not fabricate any test results.

Then STOP.

Do not implement Prompt 2 yet.

Show me:
- solution structure
- projects created
- packages added
- build result
- test result
- any issues encountered
```

---

# Prompt 2 — Domain Model + Canonical Hashing

```text
Implement ONLY Delivery Task 2: Domain model and canonical hashing.

The solution has already been scaffolded.

Before coding:
1. Inspect the existing solution.
2. Inspect plan.md.
3. Inspect the assessment requirements.
4. Reuse the existing projects.
5. Do not recreate the solution.
6. Do not create duplicate projects.

This task is ONLY about:
- Audit event domain model
- Domain validation
- Canonical serialization
- SHA-256 hashing
- Genesis value
- Hash-chain rules
- Unit tests
- Documentation of canonical hashing

Do NOT implement:
- PostgreSQL persistence
- EF Core database mappings
- API endpoints
- concurrency
- advisory locks
- retention
- redaction
- export
- compliance reporting

Create the domain model for an immutable audit event containing:
- EventId
- SequenceNumber
- EventType
- ActorId
- ResourceType
- ResourceId
- Payload
- Timestamp
- PreviousHash
- ContentHash

Use plan.md decisions:
- timestamp is server-assigned UTC
- sequence number is assigned by the append process
- clients must not control the chain sequence

Implement deterministic canonical JSON serialization.

Document and enforce:
- deterministic property ordering
- deterministic property names
- UTF-8 encoding
- timestamp format
- null handling
- numeric representation
- deterministic payload representation

Create an abstraction such as ICanonicalEventSerializer.

Implement SHA-256 hashing through an abstraction such as IEventHasher.

The hash input must include:
- EventId
- SequenceNumber
- event fields
- PreviousHash

Define a deterministic genesis value. Do not generate a random genesis value.

The chain is:

Record 1:
PreviousHash = GENESIS
ContentHash = SHA256(CanonicalRecord1)

Record 2:
PreviousHash = ContentHash(Record1)
ContentHash = SHA256(CanonicalRecord2)

Record 3:
PreviousHash = ContentHash(Record2)
ContentHash = SHA256(CanonicalRecord3)

Validate obvious invalid values:
- missing EventId
- missing EventType
- missing ActorId
- missing ResourceType
- missing ResourceId
- invalid SequenceNumber
- invalid timestamp
- invalid hash values where appropriate

Do not introduce unsupported business rules.

Add unit tests for:
1. Same event produces same canonical representation.
2. Same event produces same SHA-256 hash.
3. EventId change changes hash.
4. SequenceNumber change changes hash.
5. EventType change changes hash.
6. ActorId change changes hash.
7. ResourceType change changes hash.
8. ResourceId change changes hash.
9. Payload change changes hash.
10. Timestamp change changes hash.
11. PreviousHash change changes hash.
12. Genesis handling.
13. Deterministic property ordering.
14. Deterministic payload serialization.
15. Unicode payload handling.
16. Null handling.

Create:
docs/canonical-hashing.md

Document:
- purpose of hash chain
- fields included
- canonical JSON rules
- UTF-8
- SHA-256
- genesis value
- PreviousHash
- ContentHash
- deterministic serialization
- limitations

Do not claim tampering is impossible.

Run dotnet build and all unit tests.

Fix actual failures only.

Do not fabricate results.

STOP. Do not implement PostgreSQL yet.
```

---

# Prompt 3 — PostgreSQL + EF Core

```text
Implement ONLY the PostgreSQL persistence portion of Delivery Task 2.

Read:
- plan.md
- audit-log-service-assessment-requirements.md
- current implementation

Do not modify the domain hashing design unless a real defect is found.

Use:
- PostgreSQL
- EF Core
- Npgsql

Create the AuditEvents database model.

Persist:
- EventId
- SequenceNumber
- EventType
- ActorId
- ResourceType
- ResourceId
- Payload
- Timestamp
- PreviousHash
- ContentHash

Also create chain metadata required for the global chain head.

Add constraints preventing:
- duplicate EventId
- duplicate SequenceNumber

Add indexes supporting:
- sequence ordering
- actor filtering
- resource type/id filtering
- event type filtering
- timestamp filtering

Use JSONB for structured payload where appropriate.

Create EF Core migrations.

Add PostgreSQL integration-test infrastructure.

Do NOT implement the append API.
Do NOT implement advisory locking.
Do NOT implement retention, redaction, export, compliance, or load testing.

Add integration tests for:
- valid persistence
- duplicate EventId rejection
- duplicate SequenceNumber rejection
- retrieval in sequence order

Run dotnet build and relevant tests.

Report actual results only.

STOP.
```

---

# Prompt 4 — Transactional Append + Advisory Lock

```text
Implement the append operation.

Read plan.md carefully.

The selected concurrency design is:

GLOBAL POSTGRESQL TRANSACTION-SCOPED ADVISORY LOCK

Maintain one globally ordered hash chain.

Implement atomically:

BEGIN TRANSACTION
1. Acquire PostgreSQL transaction-scoped advisory lock.
2. Read current chain head.
3. Determine next SequenceNumber.
4. Determine PreviousHash.
5. Construct immutable event.
6. Canonically serialize it.
7. Calculate ContentHash.
8. Insert event.
9. Update chain-head metadata.
10. COMMIT.

Requirements:
- no duplicate sequence numbers
- no duplicate predecessors
- no forked chain
- no partial event on failure
- no partial chain-head update
- rollback restores previous valid state

Use database transaction semantics correctly.

Do not use an in-memory lock as the correctness mechanism.
Do not introduce a queue or distributed service.

Add tests for:
- sequential append
- multiple appends
- correct sequence numbers
- correct PreviousHash
- correct ContentHash
- rollback
- database failure
- concurrent append behavior

Document:
- global advisory-lock decision
- correctness benefit
- simpler verification
- single global write bottleneck
- horizontal write scalability trade-off

Do not implement HTTP endpoints yet.

Run tests.

STOP.
```

---

# Prompt 5 — Scenario A APIs

```text
Implement Scenario A APIs.

Create Minimal API endpoints:

POST /api/v1/audit-events
GET /api/v1/audit-events
GET /api/v1/audit-events/verify

POST must accept:
- eventType
- actorId
- resourceType
- resourceId
- payload

Server assigns:
- authoritative UTC timestamp
- sequence number

Client must not control:
- ContentHash
- PreviousHash
- SequenceNumber

There must be no update endpoint.
There must be no delete endpoint.

GET query must support:
- actor
- resource type
- resource ID
- event type
- inclusive start timestamp
- inclusive end timestamp
- stable cursor pagination

Verification must report:
- valid/invalid
- first inconsistent event identifier
- sequence information
- violation type

At minimum detect:
- ContentHash mismatch
- PreviousHash mismatch
- sequence gap
- duplicate sequence
- invalid genesis relationship

Add API/integration tests.

Do not implement Scenario B, C, or D load testing yet.

Run build and tests.

STOP.
```

---

# Prompt 6 — Scenario A Tampering Proof

```text
Complete Scenario A validation.

Do not add unrelated business functionality.

Create integration tests that:
1. Write multiple events.
2. Verify valid chain.
3. Modify one stored record directly in PostgreSQL.
4. Verify again.
5. Confirm tampering is detected.
6. Confirm earliest inconsistent record.
7. Confirm violation classification.

Also test:
- sequence modification
- PreviousHash modification
- ContentHash modification
- deleted record
- invalid predecessor relationship

Distinguish:
- content tampering
- chain-link tampering
- sequence failure
- missing record

Do not modify production behavior unless tests expose an actual defect.

Create:
docs/scenarios/scenario-a.md

Document:
- requirement
- design
- API
- hash chain
- verification
- test evidence
- limitations

Do not fabricate results.

Run all tests.

STOP.
```

---

# Prompt 7 — Scenario B Retention

```text
Implement Scenario B retention only.

Records older than a configurable retention window become eligible for archival.

Archive state must NOT modify immutable hashed event content.

Do not modify:
- EventId
- SequenceNumber
- EventType
- ActorId
- ResourceType
- ResourceId
- Payload
- Timestamp
- PreviousHash
- ContentHash

Store archive state separately.

Implement:
- configurable retention window
- archive eligibility
- archive operation/service
- archived-state representation

Do not physically delete records in this prototype.

Chain verification must continue to verify archived records.

Verification must distinguish:
- legitimately archived record
- missing record
- tampered record

Add tests.

Create:
docs/scenarios/scenario-b-retention.md

Document:
- retention rule
- archive behavior
- why archive state is outside hash
- why physical deletion is deferred
- verification behavior

Run tests.

STOP.
```

---

# Prompt 8 — Scenario B Structured Redaction

```text
Implement Scenario B structured redaction.

Follow plan.md.

Sensitive payload fields must be protected without modifying an already chained event.

Use:
- immutable cryptographic commitment for sensitive values
- redacted read projection
- original plaintext is not retained/exportable after commitment creation
- chain hashes the commitment-bearing immutable representation

Support configured sensitive JSON paths.

Requirements:
1. Convert sensitive values into commitments at ingest.
2. Store commitment-bearing immutable representation.
3. Return redacted representation from normal reads.
4. Never expose sensitive plaintext in exports.
5. Chain verification remains valid.
6. Non-sensitive fields remain readable.
7. Sensitive values must not appear in error responses.
8. Sensitive values must not be written to application logs.

Document:
- commitment construction
- salt strategy
- sensitive path configuration
- threat model
- limitations
- chain integrity behavior
- production key/configuration concerns

Add tests for:
- top-level sensitive field
- nested sensitive field
- multiple sensitive fields
- non-sensitive field
- redacted API response
- redacted export
- chain verification
- invalid configuration
- sensitive value not appearing in logs/errors where testable

Do not claim regulatory compliance.

Create:
docs/scenarios/scenario-b-redaction.md

Run all tests.

STOP.
```

---

# Prompt 9 — Scenario B Bulk Export

```text
Implement Scenario B bulk export.

Provide an export operation scoped by:
- resourceId
OR
- actorId

The export must be independently verifiable without database access.

Include:
- export version
- export timestamp
- filter information
- events
- EventId
- SequenceNumber
- PreviousHash
- ContentHash
- canonical hashing metadata
- chain boundary context
- verification manifest

Sensitive plaintext must never appear.

Implement an independent verification routine for the exported bundle.

Test:
1. valid export
2. independent verification
3. modified exported record
4. modified hash
5. invalid predecessor
6. first-chain segment
7. middle-chain segment
8. final-chain segment
9. empty export
10. redacted payload

Create:
docs/scenarios/scenario-b-export.md

Document the exact export format and verification process.

Run all tests.

STOP.
```

---

# Prompt 10 — Scenario C Requirement Clarification

```text
Implement ONLY the documentation portion of Scenario C first.

Do not implement the compliance report yet.

Original requirement:

"Regulators need to be able to audit access to client account data."

Create:
docs/scenarios/scenario-c-requirement-clarification.md

Identify unresolved questions concerning:
- regulator jurisdiction
- client definition
- account definition
- access event definition
- successful access
- denied access
- actor identity
- system/service identity
- time range
- timezone
- report format
- authorization
- retention
- evidentiary expectations
- export
- chain verification

Then define prototype assumptions from plan.md:
- CLIENT_ACCOUNT resources
- specified time range
- deterministic CSV/JSON
- chain-status metadata

Clearly separate:
1. Original requirement
2. Ambiguities
3. Questions
4. Prototype assumptions
5. Normalized requirement
6. In scope
7. Out of scope

Do not invent regulatory requirements.

Do not write implementation code.

STOP.
```

---

# Prompt 11 — Scenario C Implementation

```text
Implement Scenario C based ONLY on the normalized requirement in:

docs/scenarios/scenario-c-requirement-clarification.md

Implement a compliance access report.

The prototype reports access to CLIENT_ACCOUNT resources for a specified time range.

Support:
- start time
- end time
- deterministic ordering
- deterministic JSON output
- CSV output if included in documented design
- chain-status metadata

Define explicitly which event types represent account access.

Do not silently include unrelated event types.

Handle:
- empty report
- invalid time range
- boundary timestamps
- unsupported resource type
- deterministic ordering

Do not implement jurisdiction-specific certification, legal/regulatory authentication, or regulatory approval workflow unless explicitly required by the normalized prototype requirement.

Add unit and integration tests.

Create:
docs/scenarios/scenario-c.md

Run all tests.

STOP.
```

---

# Prompt 12 — Scenario D Concurrency Testing

```text
Implement Scenario D correctness testing.

Do not change the selected architecture.

Architecture:
- one global chain
- PostgreSQL transaction-scoped advisory lock
- transactional append

Create repeatable concurrent integration tests.

Use at least 50 concurrent writers.

Prove:
- no duplicate sequence numbers
- no missing sequence numbers
- no duplicate predecessors
- no forked chain
- no silently lost committed writes
- all committed events verify successfully

Also test:
- transaction rollback
- failed insert
- failed chain-head update
- database connection failure where practical
- retry behavior
- application restart/recovery where practical

After concurrent execution:
1. query all events
2. order by sequence
3. verify sequence continuity
4. verify predecessor relationships
5. verify ContentHash
6. run full verification

Do not fabricate performance metrics.

Create:
docs/scenarios/scenario-d.md

Document:
- concurrency problem
- selected solution
- correctness model
- failure model
- trade-offs

Run tests.

STOP.
```

---

# Prompt 13 — Scenario D Load Test

```text
Implement the Scenario D load-test harness.

Create it under:
tests/AuditLogService.LoadTests/

Support at least 50 parallel clients.

Measure actual:
- total requests
- successful writes
- failed writes
- elapsed time
- throughput
- p95 append latency

Also validate:
- duplicate EventId
- duplicate SequenceNumber
- duplicate PreviousHash/predecessor
- sequence gaps
- final chain verification

Produce:
- machine-readable result
- human-readable result

Do not hard-code or invent performance numbers.

Run against the real local API and PostgreSQL.

Document exactly how to execute it.

Run the load test.

Record only actual measured results.

Do not claim production performance based on this local test.

Update:
docs/scenarios/scenario-d.md

with actual measured results.

STOP.
```

---

# Prompt 14 — Scenario E Design Only

```text
Create Scenario E design documentation.

DO NOT IMPLEMENT Scenario E.

Create:
docs/scenarios/scenario-e.md

Design:
1. background integrity verification
2. verification scheduling
3. verification lag measurement
4. integrity checkpoints
5. checkpoint contents
6. signing/anchoring approach
7. metrics
8. alert thresholds
9. alert recipients
10. failure handling
11. evidence preservation
12. blast-radius analysis
13. scaling at 10x record volume
14. scaling at 100x record volume
15. operational runbook

Clearly label:
IMPLEMENTED: NO
DESIGN ONLY

Do not claim these capabilities exist.

STOP.
```

---

# Prompt 15 — Scenario F Design Only

```text
Create Scenario F design documentation.

DO NOT IMPLEMENT Scenario F.

Create:
docs/scenarios/scenario-f.md

The planned future design is a per-tenant-chain authorization model.

Cover:
1. tenant identity
2. authentication boundary
3. authorization boundary
4. per-tenant chain
5. tenant-scoped query
6. tenant-scoped verification
7. tenant-scoped export
8. cross-tenant access denial
9. count leakage
10. pagination leakage
11. error leakage
12. timing leakage
13. tenant enumeration
14. blast radius
15. operational implications
16. migration from current global-chain design

Include future negative security tests.

Clearly label:
IMPLEMENTED: NO
DESIGN ONLY

Do not claim multi-tenancy is currently implemented.

STOP.
```

---

# Prompt 16 — Security and Reliability Review

```text
Act as a senior software architect and security reviewer.

DO NOT modify the code.

Review the implementation against:
- assessment requirements
- plan.md
- hash-chain integrity
- canonical serialization
- SHA-256 calculation
- transaction atomicity
- advisory-lock concurrency
- rollback
- tamper detection
- sensitive-data exposure
- redaction
- export
- compliance reporting
- SQL/database risks
- configuration/secrets
- error handling
- logging
- denial-of-service considerations
- pagination abuse
- malformed input
- Scenario E boundary
- Scenario F boundary

For every finding provide:
Finding
Severity
Evidence
Impact
Recommended action
Assessment relevance
Implemented / Partially Implemented / Design Only / Out of Scope

Do not invent vulnerabilities.

Do not make code changes.

STOP after the review.
```

---

# Prompt 17 — Final Documentation

```text
Create final engineering documentation from the implementation that actually exists.

Do not invent functionality or test results.

Create/update:
docs/architecture.md
docs/api.md
docs/testing.md
docs/trade-offs.md
docs/final-engineering-summary.md

Architecture should cover:
- problem
- requirements
- components
- data model
- APIs
- hash chain
- canonicalization
- genesis
- PostgreSQL
- transaction model
- advisory lock
- retention
- redaction
- export
- compliance reporting
- concurrency
- failure handling
- security
- limitations

API documentation must match actual implementation.

Testing documentation must contain actual tests and actual results.

Trade-offs must clearly explain:
- global chain
- advisory lock
- write scalability trade-off
- retention choice
- redaction commitment approach
- compliance scope
- E/F scope boundaries

Final engineering summary must include:
- plan
- rationale
- implemented scenarios
- design-only scenarios
- validation
- risks
- assumptions
- limitations

Clearly distinguish:
IMPLEMENTED
PARTIALLY IMPLEMENTED
DESIGN ONLY
OUT OF SCOPE

STOP.
```

---

# Prompt 18 — Final Validation

```text
Perform final validation of the entire repository.

Do not make speculative changes.

Run:
1. dotnet restore
2. dotnet build
3. formatter/analyzer checks
4. unit tests
5. integration tests
6. Scenario D concurrency tests
7. Scenario D load test
8. local API smoke test

Verify:

Scenario A:
- append
- query
- pagination
- verification
- direct database tampering detection

Scenario B:
- retention
- archived verification
- redaction
- sensitive-data non-disclosure
- export
- independent export verification

Scenario C:
- compliance report
- documented assumptions
- time filtering
- deterministic output

Scenario D:
- 50+ concurrent clients
- no duplicate sequence
- no gaps
- no duplicate predecessor
- rollback correctness
- final chain verification

Scenario E:
- design only
- no false implementation claims

Scenario F:
- design only
- no false implementation claims

Also check:
- no secrets committed
- no personal information invented
- no fake performance numbers
- no fake test results
- no update API
- no delete API
- documentation matches implementation
- README instructions work
- ATTESTATION.md contains only placeholders until I personally complete it

Fix only real defects found.

Produce a final validation report containing actual commands and actual results.

Do not fabricate anything.

STOP.
```

---

# Prompt 19 — Test Gap Analysis

```text
Review the current test suite against the assessment requirements and plan.md.

Do not modify code yet.

Create a test coverage matrix:

Requirement | Test | Unit/Integration/Load | Status | Evidence

Specifically verify coverage for:

Scenario A:
- append
- query
- pagination
- hash calculation
- genesis
- verification
- tampering
- direct database mutation
- sequence violation
- predecessor violation

Scenario B:
- retention
- archive state
- archived verification
- redaction
- sensitive-data non-disclosure
- export
- independent export verification

Scenario C:
- report filtering
- time boundaries
- deterministic output
- empty result
- excluded event types

Scenario D:
- concurrent writes
- no duplicate sequence
- no gaps
- no duplicate predecessor
- rollback
- retry/recovery
- throughput
- p95 latency

Scenario E:
- design only

Scenario F:
- design only

Identify missing tests.

Do not fabricate test results.
Do not modify files yet.

STOP.
```

---

# Prompt 20 — Fix Test Gaps

```text
Implement only the missing tests identified in the previous test-gap analysis.

Do not change production behavior unless a test demonstrates an actual defect.

For every production-code change:
1. Explain the defect.
2. Explain why the change is required.
3. Add or update the regression test.
4. Run affected tests.
5. Run the complete test suite afterward.

Do not fabricate results.

STOP after completing the identified test gaps.
```

---

# Prompt 21 — AI Usage Traceability + Final Assessment Review

```text
Complete the final AI-assisted engineering evidence for the assessment.

Create:
docs/ai-usage.md

Use only actual evidence from this development process.

Do not fabricate prompts, AI interactions, decisions, or history.

Document, where evidence exists:
- task
- AI assistance used
- purpose of prompt
- relevant output
- what I accepted
- what I modified
- what I rejected
- why
- validation performed

Organize by:
1. requirements
2. architecture
3. implementation
4. testing
5. debugging
6. documentation
7. security review

Explicitly state that the engineer reviewed and validated AI-assisted output.

If historical AI interaction information is unavailable, write:
"Not captured"

Do not invent it.

--------------------------------------------------
FINAL ASSESSMENT REVIEW
--------------------------------------------------

After creating the AI usage document, act as the assessment review panel.

Read:
- audit-log-service-assessment-requirements.md
- plan.md
- README.md
- architecture documentation
- scenario documentation
- tests
- AI usage documentation
- Git history where available

Do not modify production code.

Evaluate whether the repository provides evidence for:
1. Requirement understanding
2. Ambiguity management
3. Task decomposition
4. Engineering design
5. Hash-chain correctness
6. Tamper detection
7. Retention
8. Privacy-compatible redaction
9. Bulk export
10. Compliance reporting
11. Concurrency correctness
12. Failure handling
13. Testing rigor
14. AI-assisted engineering
15. AI-use traceability
16. Security reasoning
17. Operational reasoning
18. Production-readiness reasoning
19. Scope management
20. Engineer ownership

For each area provide:
- Evidence found
- Missing evidence
- Risk
- Recommended action before submission

Do not give an overall score or ranking.

Do not invent evidence.

Do not fabricate test results or performance results.

STOP.
```

---

# Execution Checklist

| Step | Prompt | Purpose | Status |
|---:|---|---|---|
| 0 | Prompt 0 | Understand requirements | **Done** |
| 1 | Prompt 1 | Fresh .NET solution | ⬜ |
| 2 | Prompt 2 | Domain + hashing | ⬜ |
| 3 | Prompt 3 | PostgreSQL + EF Core | ⬜ |
| 4 | Prompt 4 | Transactional append | ⬜ |
| 5 | Prompt 5 | Scenario A APIs | ⬜ |
| 6 | Prompt 6 | Scenario A tampering | ⬜ |
| 7 | Prompt 7 | Scenario B retention | ⬜ |
| 8 | Prompt 8 | Scenario B redaction | ⬜ |
| 9 | Prompt 9 | Scenario B export | ⬜ |
| 10 | Prompt 10 | Scenario C clarification | ⬜ |
| 11 | Prompt 11 | Scenario C implementation | ⬜ |
| 12 | Prompt 12 | Scenario D concurrency | ⬜ |
| 13 | Prompt 13 | Scenario D load test | ⬜ |
| 14 | Prompt 14 | Scenario E design | ⬜ |
| 15 | Prompt 15 | Scenario F design | ⬜ |
| 16 | Prompt 16 | Security review | ⬜ |
| 17 | Prompt 17 | Documentation | ⬜ |
| 18 | Prompt 18 | Final validation | ⬜ |
| 19 | Prompt 19 | Test gap analysis | ⬜ |
| 20 | Prompt 20 | Fix test gaps | ⬜ |
| 21 | Prompt 21 | AI evidence + final review | ⬜ |

## Git Commit Guidance

Commit after each meaningful completed stage, for example:

```text
01 - Scaffold AuditLogService solution
02 - Add audit event domain model
03 - Add canonical hashing
04 - Add PostgreSQL persistence
05 - Implement transactional append
06 - Implement Scenario A APIs
07 - Add Scenario A tamper detection
08 - Implement Scenario B retention
09 - Implement Scenario B redaction
10 - Implement Scenario B export
11 - Document Scenario C clarification
12 - Implement Scenario C compliance report
13 - Add Scenario D concurrency tests
14 - Add Scenario D load test
15 - Add Scenario E design
16 - Add Scenario F design
17 - Add security review
18 - Add engineering documentation
19 - Add test gap analysis
20 - Fix test gaps
21 - Add AI usage traceability and final review
```

Do not fabricate commit history, test results, performance measurements, or AI interaction history.
