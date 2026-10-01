# AI-Assessment for Readiness: Build an AI-Assisted Software Engineering System — Audit Log Service

**Topic:** AI-Assessment for Readiness Exercise — Tamper-Evident Audit Log Service  
**Version:** 1.0  
**Date shown in assessment:** 2026-08-13  
**Classification:** Confidential & Proprietary — individual candidate assessment

> This document consolidates the requirements visible in the provided assessment screenshots. The repeated screenshots have been treated as duplicate views of the same source material and are not repeated here.

---

## 0. How to Submit & Integrity Expectations — Read First

This is an individual, confidential assessment. The evaluation considers both the system that is built and the evidence that demonstrates how it was built.

### 0.1 Do it on your local system

Develop the solution on your local machine in a local repository.

Use **GitHub Copilot** as the AI tool.

### 0.2 Do Your Own Work, on Your Own Setup

- Complete the assignment individually, on your own machine and under your own accounts.
- The submission must be your own original work.
- Do not start from, copy, or share another person's solution.
- Do not build on a shared or jointly accessed copy of the assignment.
- Keep the assignment, problem, and solution confidential.
- Do not forward, re-host, or distribute the assessment materials or solution.

### 0.3 AI Use Is Expected — Just Be Honest About It

Using AI such as Copilot, Claude, etc. is part of the exercise.

The submission must honestly represent the candidate's own process and authorship. The candidate must be able to explain and defend:

- what was built,
- how it was built,
- how AI was used during the work,
- and the engineering decisions made.

### 0.4 Attestation — Required

Add an `ATTESTATION.md` file to the root of the repository.

It must record:

- full name,
- email address,
- assignment title,
- dates started and submitted,

followed by an attestation that the submission is the candidate's own individual work, completed on their own machine and accounts, and honestly reflects their development process and use of AI.

---

# 1. Objective

Build a working prototype that transforms a set of requirements into a reviewable engineering outcome using AI-assisted engineering execution.

Demonstrate:

- requirement understanding,
- task decomposition,
- multi-step execution,
- output generation and validation,
- engineer-led execution accelerated by AI,
- and engineer ownership rather than autonomous orchestration.

---

# 2. Scenario

Build a **tamper-evident audit log service** — a system that records an append-only history of events and guarantees that past records cannot be modified or deleted without detection.

The task is to design and build the service over **2 days** using AI assistance such as Copilot / Claude / etc., while demonstrating engineering judgment at every step.

The scope guidance in Section 5 defines how time should be allocated across the six scenarios.

---

# 3. Scope

The assessment covers:

- Greenfield scenarios for a new system or feature.
- Feature extension on the candidate's own codebase.
- Testing and documentation improvements.
- Well-defined and ambiguous requirements.
- Concurrency.
- Operability.
- Multi-tenant security concerns on a system built by the candidate.

---

# 4. Core Requirements

## 4.1 Requirement Understanding

Interpret intent, identify ambiguity, and normalize requirements into a clear engineering problem.

## 4.2 Task Decomposition

Convert high-level requirements into actionable tasks with dependencies and sequencing.

## 4.3 AI-Assisted Execution — Critical Differentiator

Use AI across implementation, debugging, refactoring, test generation, documentation, and review preparation.

Requirements:

- Define tasks with intent, constraints, acceptance criteria, and technical context.
- Use disciplined prompting with iterative refinement.
- Maintain traceability of AI use, including generated / edited / rejected output and the rationale.
- Apply quality gates such as analysis, linting, tests, security checks, and performance checks.
- Enforce secure AI usage.
- Require human sign-off for high-impact changes.
- Retain explicit engineer ownership of correctness, maintainability, and production readiness.

## 4.4 Engineering Output Generation

Produce production-quality:

- code,
- API / schema definitions,
- infrastructure or integration tests,
- supporting documentation.

The design and documentation must demonstrate maintainability.

## 4.5 Validation and Risk Control

Identify:

- risks,
- trade-offs,
- failure scenarios,

and define validation and safety guardrails.

## 4.6 Controlled Oversight

The engineer leads execution and approves all output.

AI assists with tasks, but the engineer remains responsible for the result.

## 4.7 Final Engineering Summary

Include:

- plan,
- rationale,
- artifacts,
- risks,
- trade-offs,
- validation,
- assumptions,
- limitations.

## 4.8 Operational and Non-Functional Ownership

Address:

- concurrency,
- integrity monitoring,
- tenant isolation,
- production readiness.

Where a requirement involves a trade-off between correctness, performance, and privacy, state the trade-off explicitly and defend the position taken.

---

# 5. Scenario Details

## 5.1 Scope & Time Guidance

- **Scenarios A, B, and C are required.**
- Scenarios D, E, and F build on the same service.
- Attempt at least one of D, E, or F in full and provide a documented design plus an explicit scope boundary for the remainder.
- A well-reasoned, explicitly bounded partial implementation is preferable to a rushed complete implementation.
- The final summary must clearly state:
  - what was implemented,
  - what was scoped out,
  - and why the scope boundary was chosen.

---

# Scenario A — Greenfield: Core Audit Log Service

## Status

**REQUIRED**

## Objective

Build an audit log service with the capabilities below.

### A.1 Write API

Accept an event record containing, at minimum:

- `eventType` — what happened  
  Example: `USER_LOGIN`, `RECORD_UPDATED`, `PERMISSION_GRANTED`
- `actorId` — who or what caused the event
- `resourceType` — type of resource affected
- `resourceId` — specific resource affected
- `payload` — structured object containing event-specific detail
- `timestamp` — when the event occurred  
  This may be caller-supplied or server-assigned; document the choice.

The records must be append-only.

The API must **not expose an update or delete operation**.

### A.2 Query API

Retrieve events with filtering using any combination of the supported event attributes, including:

- actor,
- resource,
- event type,
- time range.

Support pagination for large result sets.

### A.3 Tamper Evidence — Hash Chain

Each stored record must include:

- a hash of its own content, based on the event fields above;
- a hash of the immediately preceding record, or a defined genesis value for the first record.

Together these form a hash chain.

Any modification to a past record invalidates its own hash and the hashes that follow it, making tampering detectable.

### A.4 Chain Verification Endpoint

Expose a GET endpoint that walks the full chain and reports:

- whether the chain is intact;
- which record is the first inconsistency;
- what type of violation was detected.

### A.5 Validation of Scenario A

The entire assignment is validated through the service APIs:

1. Write events.
2. Query events.
3. Verify the chain.
4. Modify a record directly in the data store.
5. Verify again to confirm that the modification is detected.

No external application or consumer is required.

---

# Scenario B — Extend Your Own System: Retention and Redaction

## Status

**REQUIRED**

Extend the service built in Scenario A.

## B.1 Retention Policy

Records older than a configurable window should be:

- archivable, or
- soft-deletable.

The chain verification endpoint must correctly handle archived records and must not report a false-positive chain break for records that were legitimately archived according to policy.

## B.2 Structured Redaction

Certain fields within a record's payload may contain sensitive data, for example:

- account numbers,
- personal identifiers.

Those fields must be redactable to satisfy data-privacy requirements **without breaking the hash chain**.

This is explicitly identified as a genuine engineering problem:

- the original hash covers the original value;
- simply removing or changing the value would invalidate the hash.

Design and implement a redaction scheme that satisfies both:

- tamper evidence,
- data privacy.

Document:

- the chosen approach,
- trade-offs considered,
- limitations of the chosen solution.

## B.3 Bulk Export

Provide an endpoint to export all records for a given:

- `resourceId`, or
- `actorId`

as a self-contained, verifiable bundle.

The bundle must include enough chain metadata for a recipient to independently verify that the records it contains have not been altered since export.

---

# Scenario C — Ambiguous: Compliance Reporting

## Status

**REQUIRED**

Product states:

> "Regulators need to be able to audit access to client account data."

This requirement is intentionally underspecified.

Demonstrate:

### C.1 Requirement Clarification

How the requirement is clarified and normalized before writing code.

### C.2 Ambiguities and Assumptions

Document:

- ambiguities identified;
- assumptions made;
- or questions that would be asked before proceeding.

### C.3 Technical Design

Show how the clarified requirement is translated into a concrete technical design.

### C.4 Scope Decision

Document:

- what is implemented;
- what is scoped out;
- why the scope decision was made.

### C.5 Submission Evidence

The submission must include:

- the clarified requirement statement used as the basis for implementation;
- resulting design decisions;
- implementation, or a well-reasoned partial implementation with a documented scope boundary.

---

# Scenario D — Concurrency: Chain Continuity Under Parallel Writes

The hash chain in Scenario A is inherently serial: each record's hash depends on the record immediately before it.

Real callers, however, write concurrently.

Extend the service so the chain remains correct and verifiable under parallel writes.

## D.1 Concurrent Write Correctness

Simultaneous writers must not produce:

- a forked chain,
- a duplicated chain,
- a gapped chain.

Two records must never claim the same predecessor, and no assigned write may be silently dropped.

## D.2 Design Decision and Defense

Choose an approach and justify it.

Possible approaches shown in the assessment include:

- a serialized append path, such as a queue or elected leader;
- batched commits with a `Mutex` per key;
- sharded per-key chains anchored to periodic global checkpoints.

Document:

- the approach selected;
- why it was selected;
- what was given up;
- the effect on GET/query behavior;
- relevant correctness and performance trade-offs.

## D.3 Throughput Evidence

Provide a repeatable load test that:

- continuously runs at least 50 parallel clients;
- demonstrates that the chain does not break;
- demonstrates no lost or duplicated events;
- measures write throughput;
- measures p95 latency.

Compare the measured results with the chosen concurrency approach.

## D.4 Failure Behaviour

Show what happens when:

- a write fails part-way through;
- a process dies unexpectedly.

The chain must not be left in a state where verification falsely reports tampering.

The failure and recovery behavior must be documented.

---

# Scenario E — Operations: Continuous Integrity Monitoring

A chain that is only verified when somebody remembers to call the endpoint is not an operational control.

Make integrity a monitored property of the running system.

## E.1 Background Verification

Verify the chain continuously or periodically in the background rather than requiring a full-history verification only when manually requested.

Document how the approach scales when the record count becomes approximately **10x and 100x** larger.

## E.2 Integrity Checkpoints

Periodically publish an attestable checkpoint containing information such as:

- head hash,
- record count,
- timestamp.

The checkpoint should be signed or otherwise anchored so that a verifier can establish the state of the chain at a past point in time without replaying the entire chain.

## E.3 Metrics and Alerting

Expose operational metrics such as:

- records written;
- verification lag;
- time since last successful verification;
- current chain status.

Define:

- alert thresholds;
- who receives the alert;
- what condition causes an alert.

## E.4 Operational Runbook

Provide a short operational runbook for an on-call engineer receiving a chain-break alert.

The runbook must cover:

- how to establish blast radius;
- what evidence to preserve;
- what actions should be taken;
- what should not be done.

The assessment specifically highlights that "repairing" the chain can destroy the evidence needed to understand the incident.

---

# Scenario F — Security: Multi-Tenant Isolation

The service is offered to multiple tenants whose audit data must never mix.

## F.1 Isolation Model

Choose between:

- a chain per tenant, or
- a single chain with tenant scoping.

Justify the choice against:

- tamper-evidence strength;
- query performance;
- blast radius of a single break.

## F.2 Scoped Read Paths

Every read path must be tenant-scoped, including:

- query;
- verify;
- Scenario B export.

The implementation must not leak the existence of another tenant's records, including through:

- counts;
- pagination totals;
- error messages;
- timing.

## F.3 Per-Tenant Verification

A tenant must be able to verify the integrity of its own records without:

- being granted access to another tenant's records;
- needing to trust the contents of another tenant's records.

## F.4 Authorization Tests

Include tests proving that cross-tenant access is actively denied, rather than merely being absent from the normal/happy path.

Negative tests carry more weight here than positive tests.

---

# 6. Live Defense — Scheduled After Submission

After submission, there will be a live review session with the panel.

The candidate will be expected to:

- walk through the solution;
- explain and defend design decisions;
- explain and defend AI-usage decisions;
- answer questions from the panel;
- work through a small requirement change live in the candidate's own codebase.

The environment must be ready to:

- run the application;
- modify the code.

---

# 7. Deliverables

All deliverables must be provided in the candidate's **private GitHub repository**.

## 7.1 Repository

The repository itself is shared with the panel and must contain development history.

Submission is **not** a ZIP file or a snapshot.

## 7.2 ATTESTATION.md

The attestation from Section 0.4.

## 7.3 Working Prototype

A runnable end-to-end prototype with setup instructions.

## 7.4 Architecture Overview

Include:

- components;
- data model;
- API design;
- key decisions;
- trade-offs;
- hash algorithm choice;
- chain design;
- concurrency model.

## 7.5 Scenario Documentation

Provide Scenarios:

- A,
- B,
- C,

in full, plus:

- D,
- E,
- F,

according to the scope guidance in Section 5.

Each scenario should show:

- decomposition;
- execution;
- validation.

## 7.6 Load Test Harness and Results

For Scenario D, if attempted.

## 7.7 Operational Runbook

For Scenario E, if attempted.

## 7.8 Setup Instructions

Document:

- how to run locally;
- dependencies;
- prerequisites.

## 7.9 Testing Approach, Limitations, and Trade-offs

Document:

- what is covered;
- what is not covered;
- why.

## 7.10 AI Usage Log / Traceability Notes

Document:

- what was prompted;
- what was accepted;
- what was modified;
- what was rejected.

## 7.11 Final Engineering Summary

Include:

- plan;
- rationale;
- artifacts;
- risks;
- trade-offs;
- assumptions;
- limitations.

Include an explicit scope boundary for any scenario that was not implemented in full.

---

# 8. Evaluation Criteria — High Level

The work is scored against a detailed reviewer rubric.

At a high level, the assessment evaluates:

- engineering reasoning and ambiguity management;
- system design and correctness;
- concurrency correctness under load;
- operational readiness and monitorability;
- multi-tenant security reasoning;
- effective and well-governed AI-assisted execution;
- authenticity and ownership of the work;
- code quality;
- testing and validation rigor;
- security and production readiness;
- ability to defend and adapt the solution live;
- communication.

Specific weightings and criteria are not published in the candidate materials.

The assessment rewards genuine engineering judgment that the candidate can explain and defend, rather than artifacts produced merely to match a checklist.

---

# 9. Expectation

Treat this as production-grade engineering work.

Demonstrate:

- strong design fundamentals;
- effective AI use as an accelerator;
- output ownership;
- defensible engineering reasoning;
- an authentic and verifiable development process.

## Principle

> **AI assists the engineer within tasks; the engineer owns execution, quality, and authorship.**

---

## Source Consolidation Note

The supplied screenshots contain repeated photographs of the same pages for improved visibility. This document consolidates the unique requirements from those screenshots into one requirement document and intentionally removes repeated copies of the same requirement.
