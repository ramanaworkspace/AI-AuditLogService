# Scenario C: requirement clarification

Status: documentation only. This document is a proposed basis for a future
prototype report, not a report implementation, approved regulatory specification,
or statement of compliance.

Subsequent implementation: the bounded prototype is now documented in
[Scenario C](scenario-c.md). That document resolves the access-event taxonomy,
selects JSON (not CSV), and specifies snapshot verification and failure reporting.
The sections below preserve the original clarification-stage questions and
scope; implementation does not resolve the outstanding regulatory questions.

Sources: [assessment, Scenario C](../../audit-log-service-assessment-requirements.md)
and [plan, Delivery Task 5 and key decisions](../../plan.md).

## 1. Original requirement

> "Regulators need to be able to audit access to client account data."

The assessment explicitly identifies this requirement as underspecified and asks
for clarification and normalization before code is written. It does not identify
a regulator, statute, jurisdiction, access taxonomy, or legally sufficient report.

## 2. Ambiguities

| Area | Unresolved meaning or boundary |
|---|---|
| Regulator jurisdiction | The relevant authority, jurisdiction, applicable rules, and reporting obligations are unspecified. |
| Client definition | "Client" could mean an individual, organization, customer, tenant, or another legal/business entity. |
| Account definition | Account types, account ownership, account identifiers, and which data counts as account data are unspecified. |
| Access event definition | It is unclear whether access means viewing, querying, downloading, exporting, modifying, or attempting access, including cached and indirect access. |
| Successful access | A completed request, returned data, and a person actually seeing data are different observations; none is selected. |
| Denied access | Inclusion of denied/failed attempts and the distinction between denial and technical failure are unspecified. |
| Actor identity | The required human identity, assurance, delegation, and historical identity resolution are undefined. |
| System/service identity | Background services, service accounts, and services acting for people may need separate attribution. |
| Time range | Required intervals, boundary semantics, late arrivals, and occurrence time versus recording time are undefined. |
| Timezone | The regulator's reporting timezone and daylight-saving rules are unknown. |
| Report format | Required columns, schemas, ordering, encoding, and delivery format are unspecified. |
| Authorization | Entitlement to request, view, or export reports and permitted client/account scopes are undefined. |
| Retention | Required duration, archival availability, legal holds, and deletion rules are unknown. |
| Evidentiary expectations | Required proof of integrity, provenance, completeness, authenticity, and legal certification is unspecified. |
| Export | A human-readable report and a self-contained, independently verifiable evidence bundle are different deliverables. |
| Chain verification | Verification scope, trusted checkpoints, failure handling, and acceptable evidence of verification are unspecified. |

These uncertainties cannot be resolved by treating a hash chain as proof that all
real-world access was captured or that a report satisfies regulatory obligations.

## 3. Questions

These are stakeholder questions for future clarification, not assumed legal
requirements or decisions already approved by a regulator.

| Area | Questions to resolve |
|---|---|
| Regulator jurisdiction | Which authority and jurisdiction apply? Which obligations, guidance, or prescribed report templates must the responsible stakeholders identify? |
| Client definition | Who is the client, and how is the client identified? Can a client span tenants or organizations? Must historical client relationships be resolved? |
| Account definition | Which account types and data categories are covered? Which identifier is authoritative? How are joint ownership and client-to-account relationships represented? |
| Access event definition | Which exact event types qualify? Are reads, searches, downloads, exports, writes, cached responses, and indirect access included? Which systems emit these events? |
| Successful access | What observable condition counts as successful access? Must the audit distinguish an authorized request from data actually returned or viewed? |
| Denied access | Are denied attempts included? How are authorization denial, authentication failure, and technical failure distinguished? |
| Actor identity | Which stable identity is required? How are identity assurance, impersonation, delegation, and historical names represented without disclosing unnecessary personal data? |
| System/service identity | Must both initiating human and executing service be recorded? How are unattended jobs and service-to-service access attributed? |
| Time range | Which interval is requested and are its endpoints inclusive? Does selection use occurrence or server acceptance time? How are late arrivals and repeated report runs handled? |
| Timezone | Must reports use UTC or a jurisdiction-specific timezone? Which timezone identifier and daylight-saving/ambiguous-time rules apply? |
| Report format | Are CSV, JSON, or prescribed templates accepted? Which fields, schema version, sorting, escaping, encoding, and empty-report representation are required? |
| Authorization | Who can generate, receive, and export a report? How are regulator identity, account/client scope, tenant isolation, revocation, and auditing of report access enforced? |
| Retention | What duration and legal-hold rules apply? Must archived records be reportable? How should incomplete retained history be disclosed? |
| Evidentiary expectations | Are signatures, trusted timestamps, checkpoints, provenance, custody records, or certification required? How must capture completeness and limitations be disclosed? |
| Export | Is a redacted report sufficient, or is a verifiable bundle required? What delivery, encryption, recipient access, size limits, and redaction policy apply? |
| Chain verification | Must the whole global chain or a bounded evidence segment be verified? What trusted reference is available? What must happen when verification fails or cannot finish? |

## 4. Prototype assumptions

### Plan-defined baseline

The following assumptions come from the plan, not from a regulator:

- Resource scope is `CLIENT_ACCOUNT`.
- The report covers a specified time range.
- Output is deterministic CSV/JSON.
- The report carries chain-status metadata.

The plan also excludes regulatory authentication, legal report certification, and
jurisdiction-specific rules. These exclusions are prototype boundaries, not
permission to deploy an unrestricted reporting service.

### Proposed operational interpretation

The following makes the baseline reviewable without inventing a legal mandate.
It is a proposed prototype contract, not existing behavior:

- Match the exact resource type `CLIENT_ACCOUNT`. Treat `ResourceId` as an opaque
  account identifier. This does not establish a client registry or infer account
  ownership.
- Require explicit start and end instants, with start not after end. Use
  inclusive boundaries and the server-assigned UTC audit `Timestamp`, consistent
  with the existing query convention and plan timestamp decision. Do not infer
  occurrence time from arbitrary payload fields.
- Display timestamps in UTC. Offset-bearing input, if supported by the future
  contract, must resolve to an unambiguous UTC instant; timezone-less local input
  must not be silently interpreted.
- Preserve the recorded `ActorId`; do not infer verified human identity or
  human/service relationships from it.
- Include retained archived events in the same selection: archive state does
  not alter the immutable event or remove its verification material.
- Expose only redacted payload projections, never restore committed sensitive
  values. Existing historical plaintext and unconfigured fields require separate
  privacy review.
- For an identical selected dataset, schema, and verification snapshot, produce
  identical UTF-8 output bytes: fixed column/property order, ascending
  `SequenceNumber`, invariant value formatting, explicit CSV escaping/newlines,
  and fixed JSON/null rules. Exact output schemas and CSV safety rules must be
  settled before implementation; this document does not prescribe a legal form.
- Chain-status metadata should identify the verification outcome, verified
  extent/head, and first inconsistency classification/sequence/identifier where
  available. A failure or incomplete verification must not be presented as a
  valid chain.
- Report rows and chain status must refer to a defined consistent snapshot or
  bounded head, not imply verification of writes accepted after that boundary.
  The exact mechanism remains future technical design.

### Decisions not yet made

The access-event taxonomy remains unresolved. `CLIENT_ACCOUNT` alone is not
sufficient to classify every event as access. No `EventType` names are invented
here, and no successful/denied outcome is inferred from arbitrary payloads.
Before implementing an access report, define an explicit qualifying taxonomy and
outcome representation so unrelated account events can be excluded and success,
denial, and failure can be distinguished honestly.

Identity mapping, authorization, final report schema, and verification-failure
delivery policy also require explicit design decisions. Their absence must not
be hidden by defaults or a claim of compliance.

## 5. Normalized requirement

For the bounded prototype, provide a future account-access report of recorded
events whose resource type is exactly `CLIENT_ACCOUNT`, whose event type belongs
to an explicitly agreed access taxonomy, and whose server-assigned UTC timestamp
falls within a caller-specified inclusive start/end interval.

Represent the selected events in deterministic UTF-8 CSV or JSON, ordered by
global sequence number, preserving recorded account/actor references and exposing
only permitted redacted data. Include archived retained records and chain-status
metadata tied to a defined verification boundary, reporting inconsistencies or
incomplete verification explicitly.

This is a **conditional prototype requirement**: the access taxonomy and remaining
contract/security decisions above must be settled before implementation. It is
not a jurisdiction-specific regulatory requirement. A valid chain establishes
consistency of the verified stored evidence, not completeness of access capture,
actor authenticity, lawful access, or regulatory compliance.

## 6. In scope

### This delivery

- Document the original statement, ambiguities, stakeholder questions, plan
  assumptions, normalized prototype requirement, and scope boundaries.
- Identify decisions that remain unresolved instead of inventing legal rules.
- Do not implement or claim implementation of the report.

### Future bounded prototype, after decisions are settled

- `CLIENT_ACCOUNT` access-event selection using the agreed taxonomy and explicit
  UTC interval, including retained archived records.
- Deterministic CSV/JSON report representations with redacted payload handling.
- Chain-status metadata and honest reporting of verification limitations.
- Tests for filters, time boundaries, empty results, excluded event types,
  deterministic representations, redaction, and verification outcomes.

These are proposed future deliverables, not completion claims.

## 7. Out of scope

For this documentation delivery, all report implementation, endpoints, exports,
database changes, and implementation tests are out of scope.

For the bounded prototype described by the plan, exclude:

- Jurisdiction-specific rules, invented legal retention periods, regulatory
  approval, certified reports, or assertions of regulatory compliance.
- Production regulator authentication/authorization and legal report
  certification. Production use remains blocked on these safeguards.
- Client/account registries, authoritative ownership resolution, verified
  identity federation, or human/service attribution not present in recorded data.
- Proof that source systems recorded every actual access, reconstruction of
  unrecorded events, or interpreting a valid hash chain as proof of lawful access.
- Restoration of committed plaintext or retrospective erasure of legacy
  immutable plaintext.
- Legal-hold/deletion policy changes or mutation of immutable event evidence.
- Treating a redacted CSV/JSON report as an independently verifiable evidence
  bundle. Scoped bundles, chain context, and offline manifests are separate
  Scenario B export work; the existing safe export projection is not such a
  bundle.

The scope is deliberately bounded because the original sentence and plan do not
provide the legal, identity, authorization, or evidentiary contracts needed to
make broader claims. Stakeholder answers may change these assumptions and should
be incorporated before production design or implementation proceeds.
