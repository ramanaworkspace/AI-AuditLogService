# Scenario E: continuous integrity monitoring design

**IMPLEMENTED: NO**

**DESIGN ONLY**

All workers, schedules, checkpoints, signatures, metrics, alerts, scaling
mechanisms, and operational controls below are proposals. This document does not
claim that any Scenario E capability exists or has been tested.

Sources: [assessment, Scenario E](../../audit-log-service-assessment-requirements.md)
and [plan](../../plan.md). No jurisdiction-specific obligations are assumed.

## Current boundary

The existing service supports on-demand global-chain verification using a
PostgreSQL repeatable-read snapshot. The current
[verification service](../../src/AuditLogService.Infrastructure/Verification/PostgresChainVerificationService.cs)
loads the entire ordered event history into memory and checks the chain against
persisted head metadata. Archived events remain verifiable; redacted read
projections are not hash input.

There is no implemented background verifier, scheduler, durable verification
progress, signed checkpoint publisher, external anchor, integrity metrics
exporter, alert delivery, or automatic containment. Existing health checks do
not establish chain integrity. Scenario D's local measurements do not establish
Scenario E capacity or detection latency.

## 1. Background integrity verification

Propose a separately operated read-only verifier, not work inside the append
transaction. Keep the existing one-global-chain append architecture and
transaction-scoped advisory lock unchanged.

For each run:

1. Establish one repeatable-read snapshot on the authoritative database.
2. Capture the global head sequence/hash and snapshot observation time.
3. Read immutable events in ascending sequence order through that head.
4. Check EventId uniqueness, sequence continuity, genesis, predecessor links,
   canonical ContentHash, and agreement with captured head/count.
5. Include archived events. Never hash normal redacted API projections.
6. Persist a separate run result identifying its boundary, coverage, software
   and canonicalization versions, duration, outcome, and first inconsistency.
7. Advance verified progress only after the declared range completes
   successfully. A partial scan or timeout must not become a successful run.

A full replay establishes coverage of retained history at a snapshot. A suffix
scan from a previously trusted boundary only establishes coverage of that
suffix, conditional on that boundary. It cannot detect a subsequent mutation of
an earlier record by itself. Schedule rolling historical rescans/full replays
as a separate control and expose their freshness independently.

Use a bounded streaming reader rather than the current full-history list before
claiming memory-bounded execution. This is a proposed implementation change,
not present behavior. Restrict verifier privileges to reads plus its own
progress/evidence store; it must not update audit events or chain metadata.

## 2. Verification scheduling

Proposed initial policy, subject to measurement and owner approval:

| Work | Proposed cadence | Coverage |
|---|---|---|
| New suffix verification | Every 60 seconds, with up to 10 seconds jitter | From trusted verified boundary through a newly captured head |
| Historical rescan | Every 5 minutes in bounded work units | Progressively revisit prior ranges; target a complete cycle within 24 hours |
| Full baseline replay | Before trusting initial progress, after relevant restore/format change, and weekly if capacity permits | Entire retained global history at one declared snapshot |
| Checkpoint publication | Every 5 minutes when new verified progress exists | Successfully verified boundary only |

An external scheduler or worker timer could trigger these jobs. Use a distinct
durable lease with expiry and fencing token to prevent overlapping progress
updates/publications. Do not reuse the append advisory lock for long verification
work. Schedule missed work on recovery rather than starting an unbounded backlog
of identical jobs. Record runs skipped because a lease is held.

There must be explicit database/time/CPU budgets and cancellation support.
If a cadence cannot be met, report degraded coverage and alert; do not silently
relabel suffix checks as full-history verification.

## 3. Verification lag measurement

Define separate measurements; no single "lag" value adequately describes
coverage:

- **Record lag:** observed head sequence minus last successfully suffix-verified
  sequence, for the same chain. Negative values indicate head regression or
  inconsistent progress and must raise an integrity alert, not clamp to zero.
- **Suffix success age:** current UTC time minus last successful suffix-run
  completion time.
- **Full replay age:** current UTC time minus last successful complete-history
  replay completion time.
- **Historical coverage age:** age of the oldest range's last completed rescan.
- **Unverified acceptance age:** current UTC time minus the oldest unverified
  event's server-assigned Timestamp, when such an event exists.
- **Observation freshness:** age of the head observation used to calculate lag.

Record lag can be zero in an idle database while the last scan is stale.
Likewise, frequent suffix checks can coexist with stale historical coverage.
No completed baseline means unknown progress/lag, not zero. Track checkpoint
anchoring age independently. Use monotonic clocks for durations; UTC clock
skew/negative ages are operational faults, not successful freshness.

## 4. Integrity checkpoints

Propose an append-only checkpoint stream outside immutable event rows. A
checkpoint records an asserted verified boundary and its coverage context; it
does not alter event content or create a new event chain.

Publish only after verification succeeds for the advertised coverage. For a
suffix-only result, explicitly include its trusted predecessor checkpoint and
do not assert that historical rows were freshly replayed. Do not publish a new
successful checkpoint over a known-invalid chain.

A trusted signed/anchored checkpoint lets a recipient authenticate the claimed
past head without replaying all events. Checking current records against that
claim still requires relevant chain verification; the checkpoint alone does
not prove that arbitrary records remain unchanged.

## 5. Checkpoint contents

Proposed versioned canonical signed body:

| Field | Purpose |
|---|---|
| Schema/checkpoint format version | Unambiguous parsing and signing rules |
| Environment, service instance/deployment identity, chain ID, chain epoch | Prevent cross-environment replay; distinguish explicitly authorized restore/reset histories |
| Checkpoint ID and monotonically increasing checkpoint ordinal | Identify publication and detect missing/reordered assertions |
| Previous checkpoint body digest | Link the checkpoint history |
| HeadSequenceNumber, HeadHash, verified record count | Bind the asserted global chain boundary |
| Coverage start/end sequence, mode, trusted baseline reference | Distinguish full, historical-range, and conditional suffix verification |
| Snapshot observation, verification start/completion, signing timestamps in UTC | Distinguish data boundary from publication time |
| Last full replay reference/completion and historical coverage freshness | Avoid overstating older-prefix verification |
| Verifier build, hash algorithm, event canonicalization version, policy version | Support reproducibility |
| Successful outcome and zero unresolved violations for advertised coverage | Prevent ambiguous success claims |
| Signing algorithm and key version/identifier | Select independent verification material |

An empty chain uses sequence/count zero and `GENESIS` with explicit empty-history
coverage. Sequence equals count only for a verified gapless chain starting at 1;
do not assume it for an invalid or incomplete dataset.

Keep signature bytes and external anchor receipt alongside, but outside, the
signed body to avoid circularity. Do not include plaintext payloads, credentials,
or unnecessary personal identifiers. Publish the exact canonical body bytes
and digest so recipients need not guess a serializer.

## 6. Signing and anchoring approach

Propose a versioned deterministic UTF-8 checkpoint encoding and an explicit
signature profile, initially SHA-256 with ECDSA P-256 and fixed
IEEE-P1363 signature encoding. The exact encoding/profile and test vectors must
be reviewed before implementation; this does not change event canonical hashes.

Use a non-exportable signing key in a separately controlled key-management
boundary. Separate append, verifier, and signing/publication permissions.
Signing approval should validate coverage and bounded head, not blindly sign
caller-provided JSON. Archive public verification keys, key versions, rotation,
revocation, and compromise-time evidence so historical signatures remain
interpretable.

Anchor checkpoint digest/signature in independently administered append-only
storage or a transparency/timestamp service. Store returned receipt and
publication status; retry the same checkpoint idempotently rather than inventing
a second assertion. Signatures without an independent retained reference are
weaker against database/history replacement or deletion.

An external timestamp can strengthen publication-time evidence but does not
prove event occurrence time or source identity. A compromised verifier/signer
can attest bad data; separation, immutable receipts, independent replay, and key
incident handling remain necessary. No specific cloud provider, legal trust
service, or regulatory signature mandate is assumed.

## 7. Metrics

Proposed low-cardinality metrics, not existing instrumentation:

| Metric | Type and semantics |
|---|---|
| `audit_append_acknowledged_total` | Counter of returned successful append acknowledgements; not proof of all commits |
| `audit_head_sequence` | Gauge of observed durable head, with observation timestamp |
| `audit_verifier_runs_total{mode,outcome}` | Counter: successful, inconsistent, failed, cancelled, lease-skipped |
| `audit_verifier_records_checked_total{mode}` | Counter of checked records, including rechecks; not unique record count |
| `audit_verifier_duration_seconds{mode}` | Histogram of completed/failed run duration |
| `audit_verification_record_lag` | Gauge with validity/observation freshness |
| `audit_last_suffix_success_timestamp_seconds` | Gauge used to calculate suffix success age |
| `audit_last_full_success_timestamp_seconds` | Gauge used to calculate full replay age |
| `audit_oldest_historical_scan_timestamp_seconds` | Gauge of historical coverage freshness |
| `audit_chain_integrity_status` | State: unknown, valid-at-boundary, inconsistent, stale, verifier-error |
| `audit_integrity_violations_total{type}` | Counter for classified incidents; correlate/deduplicate repeated observations |
| `audit_checkpoint_publications_total{outcome}` | Counter of signed/anchored/publication failures |
| `audit_last_anchored_checkpoint_timestamp_seconds` | Gauge of independent anchoring freshness |
| `audit_verifier_lease_skips_total` | Counter to identify scheduling contention |

Put EventIds, sequence ranges, evidence references, and run IDs in protected
incident records/logs, not metric labels. Missing worker/scrape data must trigger
monitoring-health alerts, not imply a healthy chain. Logs must avoid payload
plaintext and commitment candidate values. Durable head observations and
acknowledgement counters have different failure semantics, including ambiguous
commit acknowledgements.

## 8. Alert thresholds

All values below are **proposed initial thresholds**, not measured capabilities,
production SLAs, or legal requirements. Calibrate after capacity testing.

| Condition | Proposed threshold | Severity and action |
|---|---|---|
| Content/link/genesis/sequence/missing-record/head inconsistency | Any confirmed violation in one completed check | Critical integrity incident immediately |
| Head regression, invalid checkpoint signature, anchor conflict | Any observation | Critical; preserve evidence and investigate trust boundary |
| Suffix verification stale | Success age > 5 minutes for 2 consecutive observations | Warning; > 15 minutes critical monitoring outage |
| Oldest unverified acceptance | Age > 5 minutes for 2 observations | Warning; > 15 minutes critical coverage gap |
| Record backlog | > 10,000 records for 5 minutes | Warning; > 100,000 for 15 minutes critical capacity issue |
| Historical rescan freshness | Oldest range age > 24 hours | Warning; > 48 hours critical historical-coverage outage |
| Full replay freshness | > 8 days | Warning; > 14 days critical under the proposed weekly schedule |
| Checkpoint anchoring freshness | > 15 minutes while new verified progress exists | Warning; > 60 minutes critical trust-evidence outage |
| Repeated execution failures | 3 consecutive failed runs | Warning; no successful suffix run for 15 minutes critical |
| Worker/metrics missing | No heartbeat/observation for 5 minutes | Warning; 15 minutes critical |

An integrity violation is not the same as a verifier/database outage. Missing
data, timeouts, and incomplete checks yield unknown/error status, not tampering
or valid status. Deduplicate alerts by chain/epoch, violation type, and earliest
sequence; do not suppress a newly earlier inconsistency. A later healthy result
must not automatically close an integrity incident without evidence review.

## 9. Alert recipients

Proposed role-based routing; real contacts and escalation schedules must be
configured and tested before operational use:

- Critical integrity: security incident responder and audit-service on-call,
  with database on-call engaged immediately.
- Lag/worker/database execution failure: audit-service on-call; escalate to
  database/platform on-call for saturation, replication, or availability issues.
- Signing/anchoring failure: key-management/platform owner and security on-call.
- Sustained coverage gaps or possible downstream impact: service owner and
  designated evidence/reporting owner through the incident coordinator.

Use primary/backup paging, acknowledgement tracking, and an escalation after a
proposed 10-minute unacknowledged critical alert. Do not invent real addresses
or automatically notify regulators; external disclosure is a separate authorized
decision. Alerts contain safe metadata/evidence links, not raw audit payloads.

## 10. Failure handling

- Database/network/time-budget failure: retain prior successful boundary,
  record failed/incomplete coverage, release resources/lease, retry with bounded
  backoff and jitter. Do not advance progress.
- Replica verification, if adopted: record observed replay position and lag;
  a replica result cannot assert coverage of a newer primary head.
- Worker restart/lease loss: resume from durable trusted progress; fencing
  prevents a stale worker overwriting progress or publishing conflicting results.
- Signing/anchor unavailable: retain an immutable pending successful result;
  retry publication without falsely reporting it anchored. Alert separately.
- Integrity violation: freeze advancement of the affected successful
  checkpoint stream and initiate incident response. Preserve failed run details.
  Whether to pause append/report delivery is an explicit incident decision,
  not an implemented automatic action.
- Progress/anchor store unavailable or trust uncertain: show unknown/stale
  status and reestablish a trusted baseline; do not trust a database-local
  mutable progress row as an independent checkpoint.

Rollback of verifier metadata must not rewrite event evidence. Retry limits,
incident decisions, and progress state transitions require tests before rollout.

## 11. Evidence preservation

Before repair/restoration, propose preserving:

- Exact snapshot/head, violation classification, earliest affected sequence,
  known EventId or explicit unavailable identifier, and verifier run/software
  version.
- Restricted forensic copies of affected immutable rows and their predecessor/
  successor context, head/progress metadata, archive state, and projection state.
- Canonical bytes and independently recomputed digests where safe; retain the
  stored commitment-bearing representation, never reconstructed plaintext.
- Signed checkpoints, anchor receipts, verification keys, key lifecycle evidence,
  prior run results, alerts, and acknowledgement/escalation history.
- Relevant database logs, access/DDL history, deployment/configuration changes,
  backup/PITR references, and WAL evidence where available and authorized.

Hash evidence packages, record collection time/operator/source/snapshot, and
store copies in separately access-controlled immutable storage with a custody
log. Hashing collected evidence does not establish its pre-collection truth.
Raw historical payloads may contain sensitive data; restrict forensic access,
do not attach them to ordinary tickets, and use approved evidence-retention rules
rather than inventing durations here.

## 12. Blast-radius analysis

The global chain crosses actors/resources. Start at the earliest inconsistency:

1. Compare the last independently trusted checkpoint and historical rescan
   evidence with the captured incident head.
2. Identify the last confirmed-good predecessor and suspect sequence interval.
   A suffix mismatch can result from an earlier changed hash; distinguish root
   mutation from downstream link symptoms.
3. Map implicated rows to resources/actors and reports/exports produced over the
   interval. Do not equate chain impact with proven plaintext disclosure.
4. Include missing rows, archived evidence, metadata-only corruption, signer/
   progress compromise, and possible whole-chain replacement in hypotheses.
5. If trust evidence is absent or compromised, widen uncertainty to the retained
   chain/history rather than asserting a narrow boundary.

A checkpoint is evidence of an asserted boundary, not proof that all earlier rows
remain intact now. An attacker who rewrites a self-consistent suffix or entire
chain may require a retained independent checkpoint to detect. Document known,
suspect, and unassessed intervals separately.

## 13. Scaling at 10x record volume

Let N be current retained records, R the measured verification rate, and W the
append rate. These are planning variables, not measured Scenario E numbers.
Full replay work is O(N); at 10N it is roughly 10x work at unchanged R, not a
guarantee of exactly 10x elapsed time.

Propose bounded streaming reads, indexed sequence ranges, conditional suffix
verification, historical rescan budgets, and independently anchored progress.
Measure database I/O, CPU, connection demand, snapshot lifetime, and append
latency impact before choosing batch sizes/cadence. Require effective suffix
verification capacity to exceed W with headroom; otherwise backlog grows.

Long repeatable-read snapshots can hold back vacuum and increase storage pressure.
For historical work, immutable evidence copies anchored to trusted boundaries or
shorter range checks with explicit coverage semantics may be preferable. Merely
paging separate snapshots is not equivalent to a full consistent replay when
privileged mutation is possible. A read replica is an option only with declared
replication lag and periodic authoritative checks.

Retain the global append architecture. Read isolation/scaling does not remove its
single-write bottleneck.

## 14. Scaling at 100x record volume

At 100N, do not assume full in-memory scans or the proposed cadence remain viable.
Use measured replay duration and evidence freshness to redesign work allocation:

- Stream and partition verification work into explicit sequence ranges with
  start predecessor, end hash, and trusted boundary references.
- Validate continuity/count/uniqueness within each range and coordinator-checked
  joins between ranges; workers must read one common consistent snapshot or
  verified immutable evidence snapshot, not unrelated live snapshots.
- Parallelize historical evidence replay only where storage/CPU capacity and
  shared snapshot semantics support it. Full-chain certification still requires
  every range and join, not just the latest range.
- Consider read replicas, dedicated evidence storage, and separately retained
  signed range manifests. They are new proposed infrastructure requiring trust,
  restore, privacy, and cost evaluation, not existing features.
- Schedule historical and suffix budgets separately and expose degradation if
  complete-history coverage deadlines cannot be met. Do not silently remove
  archived rows or physically delete verification material.

Headroom, partition sizes, worker counts, and alert limits must come from measured
tests at representative volume. No throughput numbers, 100x capacity guarantee,
sharded append chains, or horizontal write scalability are claimed.

## 15. Proposed operational runbook

**DESIGN ONLY:** paging, evidence storage, signed checkpoints, and containment
controls below are not deployed procedures.

1. **Acknowledge and classify.** Record alert/run/chain/epoch and timestamps.
   Separate confirmed integrity violations from stale/error monitoring.
2. **Preserve first.** Capture read-only incident evidence and independent
   checkpoint receipts before any state-changing action. Restrict access.
3. **Confirm against a declared boundary.** On a controlled read-only snapshot,
   use the existing verifier or an independently reviewed replay. Record outcome
   and head. A fresh successful check does not erase the earlier observation.
4. **Establish blast radius.** Follow Section 12; compare trusted references,
   identify earliest inconsistency, and assess affected reports/resources and
   unknown history.
5. **Engage owners.** Notify security/database/service owners through the incident
   coordinator. Protect key/anchor evidence and investigate privileged access.
6. **Contain deliberately.** Owners decide whether to pause append, checkpoint
   publication, or report/export delivery and how to mark evidence untrusted.
   These controls require future implementation; do not pretend they exist.
7. **Diagnose without mutation.** Examine row/link/head evidence, connection and
   worker failures, deployments, credentials, restore history, and clock/replica
   conditions. Keep outage and tampering hypotheses distinct.
8. **Recover under approval.** Restore only to a separate forensic/recovery copy
   initially; verify against independent evidence. If a production restore or
   new chain epoch is approved, preserve the original, document discontinuities,
   and never silently relabel it as uninterrupted trusted history.
9. **Close with evidence.** Require successful declared-coverage replay, verified
   checkpoint/anchor publication if implemented, coverage-gap disposition, owner
   approval, and incident follow-up. Test prevention/detection changes separately.

**Do not:** update payloads/hashes/sequences to make verification pass, fill gaps
with invented events, delete suspect rows, reset the head/GENESIS, overwrite
checkpoints, discard failed run evidence, rotate/delete keys without preserving
their incident history, or claim a report is certified because the chain verifies.
"Repairing" the chain can destroy the evidence needed to understand the incident.

## Adoption gates and unresolved decisions

Before any implementation or operational claim: approve owners/escalation
contacts, threat/trust boundaries, signing/anchor provider, schema/test vectors,
coverage SLOs, capacity budgets, privacy/evidence retention, restore semantics,
and containment authority. Validate tamper/missing-record detection, scheduler
overlap, crash recovery/fencing, stale metrics, key/anchor failure, snapshot
consistency, and 10x/100x representative workloads.

**IMPLEMENTED: NO — DESIGN ONLY.** This delivery creates only this document.
