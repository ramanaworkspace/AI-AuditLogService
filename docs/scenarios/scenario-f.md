# Scenario F: per-tenant-chain authorization design

**IMPLEMENTED: NO**

**DESIGN ONLY**

This document proposes future multi-tenant isolation. Authentication, tenant
authorization, tenant chains, scoped cursors, database isolation policies, and
security tests described below do not currently exist.

Sources: [assessment, Scenario F](../../audit-log-service-assessment-requirements.md)
and [plan](../../plan.md).

## Current boundary and design choice

The current service has one global chain, one global head, a fixed global
PostgreSQL advisory lock, and no implemented tenant identity or authentication/
authorization boundary. ActorId and ResourceId are event data, not tenant
credentials. Current verification and report chain metadata describe global
history. Do not expose this prototype as an isolated multi-tenant service.

Choose **one independently verifiable chain per tenant**, rather than filtering
a shared chain:

- A tenant can replay its own chain without reading another tenant's events or
  trusting hidden cross-tenant predecessor contents.
- Tenant-leading indexes support scoped reads without cross-tenant totals.
- A single tenant's chain break has a narrower evidentiary blast radius.
- Independent heads permit different tenants' appends to proceed concurrently.
  Ordering remains serialized within each tenant; cross-tenant total ordering
  is deliberately lost.

This is a future architecture change, not a change to today's global-chain
implementation. A per-tenant chain does not by itself enforce authorization or
protect against a compromised shared database, application, or signing key.

## 1. Tenant identity

Propose an opaque, stable internal TenantId assigned through controlled
provisioning. Map an authenticated issuer/subject and verified membership to
that identifier. Do not derive tenancy from ActorId, resource names, email
domains, caller-supplied payload, or an unverified tenant header.

Select one active tenant per request. A user with multiple memberships must
select a permitted tenant explicitly; no global/default tenant fallback.
Treat service principals separately and grant explicit tenant/action scopes.
Membership removal, tenant suspension, and credential revocation need defined
freshness semantics. Preserve historical TenantIds after renames; never recycle
an identifier to a different tenant.

Tenant discovery/provisioning is a separately authorized control-plane operation,
not an unauthenticated list API.

## 2. Authentication boundary

Propose authentication middleware that validates signature, allowed algorithms,
issuer, audience, expiry, and required claims against configured trust.
Reject missing, malformed, expired, wrong-audience, and untrusted tokens before
accessing tenant data. Tenant selection is not authentication.

Resolve tenant membership from trusted claims plus an authoritative membership
policy as appropriate. Never trust externally supplied gateway identity headers
unless the gateway-to-service channel is authenticated and spoofable headers
are removed. Background jobs must authenticate as constrained service identities;
they do not inherit ambient superuser access.

Identity provider, token profile, revocation checks, and membership caching are
unresolved production decisions. No provider or regulatory identity assurance
requirement is assumed here.

## 3. Authorization boundary

Require an immutable request-scoped TenantContext created only after
authentication and tenant-membership authorization. Deny by default if it is
absent or ambiguous. Authorize both tenant and action: append, query, verify,
report, export, archive administration, or privileged operations.

Carry that context through application services and repositories. A route/body/
query TenantId, if exposed, must match the authorized context; it cannot override
it. Do not infer permission from possession of an EventId or cursor.

Propose defense in depth with PostgreSQL row-level security (RLS) on events,
heads, archives, read projections, export jobs, and verification/checkpoint state.
Use a non-owner, non-superuser runtime role without BYPASSRLS; evaluate FORCE RLS
and policy behavior. Set tenant context transaction-locally through a constrained
data-access boundary so pooled connections do not retain prior tenant state.
RLS must also protect writes through appropriate WITH CHECK policies.

Application authorization remains essential: a shared runtime credential able to
select arbitrary tenant context is not a defense against complete application
compromise. Migration, backup, verifier, and administrator roles need separate
permissions and audited exceptional access.

## 4. Per-tenant chain

Propose a versioned immutable event format that includes TenantId and chain
epoch in the canonical hash input, alongside the existing immutable fields.
Existing hashes must remain on their original format; do not reinterpret them.
Tenant/epoch binding prevents a copied record from being accepted under another
tenant's hash domain.

For each tenant/epoch:

- Sequence starts at 1 and uses a documented deterministic genesis relationship.
  Tenant/epoch inclusion in canonical input separates otherwise identical
  first records; a versioned domain-specific genesis is also an option to settle
  before implementation.
- Head metadata is keyed by tenant/epoch.
- Append takes a transaction-scoped lock scoped to that chain, reads its head,
  derives sequence/predecessor, hashes/inserts, updates head, and commits atomically.
- Preserve unique EventId and unique `(TenantId, Epoch, SequenceNumber)`.
  Scope references/FKs to prevent cross-tenant archive/projection attachment.
- Define a deterministic lock-key allocation/derivation. Possible lock-key
  collisions must at worst serialize unrelated tenants, never merge heads or
  bypass locking. Test provisioning and simultaneous first appends.

There is no cross-tenant predecessor and no promised global sequence. Retention
metadata remains outside hashed content; tenant policies/projections must never
mutate a chained event.

## 5. Tenant-scoped query

Apply TenantContext before actor/resource/type/time filters and pagination.
Use tenant/epoch-leading sequence indexes and tenant-leading filter indexes.
Never materialize all tenants' events and filter them in memory.

Resolve direct EventId lookups with both tenant and identifier. Archive flags,
redacted projections, and related metadata must use the same scope.
Scenario C reports must select tenant account events and expose only that
tenant's chain status, not the current global verifier metadata.

Include tenant, epoch, authorization scope, filter set, and policy/schema version
in cache keys. Avoid shared caches containing tenant data under URL-only keys.
Define authorization for finer account/resource scopes separately; tenant
membership need not imply access to every resource.

## 6. Tenant-scoped verification

Capture the tenant/epoch head and immutable records in one consistent snapshot.
Walk only that chain, including archived records, and report only its counts,
boundary hashes, violation metadata, and freshness.

A tenant must be able to replay its evidence without other tenant rows. If
checkpoints from the [Scenario E design](scenario-e.md) are adopted, sign bodies
bound to tenant/epoch/environment/format and expose only authorized tenant
checkpoints. Do not leak another tenant's violation through shared status.

Background fleet verification requires explicit service authorization, scoped
jobs, per-tenant progress, and controlled operator access. No failed tenant scan
may advance a different tenant's checkpoint.

## 7. Tenant-scoped export

Authorize tenant and export action at job creation, execution, retrieval, and
download. Bind job/storage identifiers and access tokens to tenant, principal/
scope, expiry, and permitted operation. Recheck revocation according to a
documented policy; do not rely only on creation-time authorization.

Select only tenant-scoped events and predecessor/successor chain context.
Include tenant/epoch and canonical format in manifests and verification material.
Any boundary record must belong to the same tenant.

Use redacted/commitment-bearing representations according to an explicit
export contract; never restore committed plaintext. A redacted read projection
alone cannot reproduce ContentHash. Independently verifiable bundles remain
separate design/implementation work, not a current capability.

Storage prefixes, object access, signed URLs, export status, counts, and error
paths all need tenant isolation. Do not expose a global object path or indefinitely
reusable bearer link as the authorization mechanism.

## 8. Cross-tenant access denial

Actively reject tenant A credentials selecting tenant B or accessing B's event,
head, verification, export job, checkpoint, or report. No fallback to global
scope for absent/invalid tenant context.

Use 401 for unauthenticated requests, and a consistent 403 for disallowed tenant
selection without revealing whether the target tenant exists. For a scoped
object lookup, propose indistinguishable 404 responses for nonexistent and
not-owned objects after authentication. Exact HTTP policy must be consistent
across paths and tested; response uniformity never replaces authorization.

Privileged cross-tenant administration must be a separate, explicitly granted,
audited operation. It must not be an unguarded flag on normal endpoints.

## 9. Count leakage

Return only authorized tenant/filter counts, if counts are offered at all.
Do not reveal global head sequence, event totals, export totals, aggregate
verification progress, other tenants' failures, or global archive counts.

Per-tenant sequence positions disclose activity within that tenant to an
authorized reader; if readers have narrower resource scopes, even tenant-wide
positions/counts may exceed their entitlement and require further policy.
Operational fleet metrics must be restricted to operators rather than published
through tenant APIs.

## 10. Pagination leakage

Propose authenticated opaque cursors containing tenant/epoch, filter digest,
ordering/boundary, authorization-scope version, expiry, and format version.
Use authenticated encryption if cursor contents must remain confidential;
Base64 encoding or a signature alone does not hide contents.

Reject tampered, expired, mismatched-filter, wrong-tenant, or wrong-epoch cursors
with the same safe validation response. Validate tenant binding before data
query. Do not trust a cursor's claimed tenant instead of TenantContext.

Next-page presence and totals must depend only on authorized rows. Avoid global
sequence cursors, gaps caused by hidden tenants, global page totals, and cache
reuse across tenants. Key rotation and cursor revocation require explicit rules.

## 11. Error leakage

Use safe Problem Details without SQL text, tenant names, identifiers from other
tenants, constraint details, payloads, stack traces, or foreign checkpoint data.
Authentication/authorization failures should not disclose target existence.

Distinguish integrity failures visible to the authorized tenant from operator-only
diagnostics. Protected logs may record the requesting principal and authorized
tenant for investigation, but logs/traces/tickets also need access controls,
retention, and payload-redaction rules.

Database/RLS errors must be handled explicitly, not turned into a misleading
successful empty report. Do not expose differences in provider exception detail
as an enumeration channel.

## 12. Timing leakage

Authorize before target lookup and keep denial responses on a common code path.
Use tenant-leading indexes, bounded queries, isolated caches, per-tenant
rate/concurrency budgets, and protected backend diagnostics.

Measure denial latency distributions for existing foreign versus nonexistent
targets across query/verify/export paths. Investigate statistically distinguishable
signals under a controlled repeatable workload. Do not invent latency tolerances;
define them with security/operations owners before testing.

Shared database/cache/CPU contention can reveal aggregate activity indirectly.
Response padding alone is not proof of constant-time behavior and can amplify
denial-of-service load. Stronger guarantees may require dedicated tenant
infrastructure; document residual side channels rather than claiming zero leakage.

## 13. Tenant enumeration

Do not offer public tenant listing, existence checks, membership introspection,
or distinguishable tenant-selection errors. Opaque identifiers reduce guessing
but are not authorization.

Audit and rate-limit repeated invalid tenant/object/cursor selections by
principal and safe request metadata. Avoid exposing suspension/deletion reasons
or provisioning status to unauthorized callers. Control invitation, account
recovery, administrative discovery, and identity-provider metadata separately.

## 14. Blast radius

An event-chain break is initially scoped to the implicated tenant/epoch and its
dependent reports/exports/checkpoints. Other tenants' chains have no predecessor
dependency on that chain and can be verified independently.

A shared credential, runtime, database administrator, signing key, backup store,
or authorization defect can affect many tenants. Widen incident scope when the
trust boundary is shared; per-tenant hashing is not per-tenant infrastructure
isolation. Tenant-specific keys can narrow some signing compromise impact but
add lifecycle cost and do not fix shared database compromise.

Follow the evidence-preserving [Scenario E runbook design](scenario-e.md).
Do not rehash/reassign events to hide a cross-tenant incident or "repair" a chain.
Record both known impact and unassessed tenants.

## 15. Operational implications

Propose:

- Explicit tenant provisioning, suspension, deletion/retention decisions, and
  chain-epoch lifecycle; preserve evidence rather than reusing TenantIds.
- Per-tenant heads, locks, verifier scheduling, checkpoints, and export quotas.
  Fair scheduling must prevent a large tenant starving small tenants.
- Operator metrics with controlled cardinality/access; tenant-facing status
  remains scoped. Handle many idle chains without scanning all history each tick.
- Audited break-glass access with approval/expiry and independent evidence.
- Backup/restore procedures that preserve tenant mapping, canonical versions,
  keys, checkpoints, and custody history; shared restore affects all tenants.
- Tests for pooled connections, service-context switching, RLS role configuration,
  caches, background-job scope, revocation, and migrations.

Identity provider, membership freshness, quotas, key isolation, deployment
partitioning, operational owners, and incident notifications require decisions.
No current health check or global metric should be relabeled tenant-isolated.

## 16. Migration from the current global chain

Do not rewrite TenantId into existing immutable events, repartition their
predecessors, or recompute hashes while claiming unchanged original evidence.
The current global chain contains no authoritative hashed tenant ownership and
may interleave future tenants' records.

Proposed controlled transition:

1. Inventory current evidence, backups, exports, schemas, and identity/resource
   ownership. Obtain authoritative ownership mapping; ambiguous rows remain
   restricted/unassigned, never guessed from ActorId or payload.
2. Preserve and verify the original global history and head. If independent
   signed/anchored evidence is needed, provision it explicitly; Scenario E is
   not implemented and cannot be assumed available.
3. Introduce a separately versioned tenant-bound format/schema and tenant
   authorization boundary; test it without altering legacy hashes.
4. Establish a documented cutover with a controlled append pause/drain and
   recorded final legacy boundary. Avoid non-atomic dual writes to unrelated
   chains. If uninterrupted migration is later required, design durable routing/
   deduplication explicitly rather than assume it.
5. Create each tenant/epoch head and deterministic genesis. New writes use
   server-resolved tenant identity and tenant-scoped transactions only.
6. Keep legacy history in restricted evidence storage with original verification
   semantics. A tenant-filtered legacy projection is not a self-contained
   tenant-verifiable chain and must be labeled accordingly.
7. If authorized historical attestations are needed, create new explicitly
   derived tenant evidence/manifest references to preserved originals under a
   reviewed privacy and trust contract. Do not expose interleaved other-tenant
   records as proof, and do not call derived evidence the original chain.
8. Gate report/export paths on format and authorization; disable unscoped
   public reads of legacy global metadata. Test reconciliation and rollback/
   routing behavior before cutover approval.

Existing events cannot acquire independent per-tenant verification merely by
filtering them. Historical trust versus cross-tenant privacy is an unresolved
migration decision requiring owner approval. Rollback after new tenant writes
must preserve both histories, not silently fold them back into a global chain.

## Future negative security tests

**PROPOSED ONLY — NOT IMPLEMENTED OR EXECUTED.**

Seed tenants A and B with deliberately different event counts, payloads, archive
states, chain failures, and export jobs. Use A credentials for every attack
below; also test unauthenticated and revoked identities.

| Negative case | Required future assertion |
|---|---|
| Spoof tenant in header/route/query/body/payload | Active denial; no B append/read/head change; no global fallback |
| Wrong issuer/audience/signature, expired token, missing tenant membership | Authentication/authorization rejection before tenant data access |
| Revoked membership or tenant suspension | Denied within documented revocation window, including existing jobs/cursors |
| B EventId with A credentials | Same scoped not-found behavior as unknown EventId; no B content/metadata |
| B verification/report/checkpoint target | Denied; no B head, counts, violation detail, or status |
| B export job/status/object/download link | Denied at each stage; no artifact or boundary-row disclosure |
| B cursor replay, filter change, expiry, byte tampering, epoch change | Safe consistent rejection; no B query and no skipped authorization |
| Count/next-page oracle with B-only record changes | A counts/items/page visibility unchanged; no global totals or gap leakage |
| Foreign versus unknown tenant/object errors | Same public status/schema/detail under the defined denial policy |
| Timing-based target existence probe | No disallowed distinguishable signal within preapproved statistical criteria; residual shared-load effects documented |
| Tenant-list/provisioning enumeration | No unauthorized discovery; abuse detection/rate policy exercised |
| Pooled connection reuse A then B then no context | No stale A/B rows; absent context denied by service and database policy |
| Direct repository/raw SQL under runtime role | RLS denies foreign reads/writes; role cannot bypass policy; transaction context cannot leak |
| Cross-tenant archive/projection/FK or head update | Database constraint/policy denial and complete transaction rollback |
| Copy B event into A chain | Tenant/epoch-bound hash/verification rejects transplanted evidence |
| Corrupt B chain while verifying A | A result remains independently scoped; A cannot infer B failure |
| Cache/job context collision | No response/artifact reuse across tenant or authorization scopes |
| Concurrent A/B writes and first-tenant provisioning | No mixed records/heads; independent gapless chains; isolated rollback |
| Legacy migration row with uncertain ownership | Restricted handling; no guessed assignment or foreign predecessor export |
| Break-glass/admin permission absent | Normal caller cannot activate privileged cross-tenant paths |

Test both HTTP and lower service/database boundaries, including logs and traces
where observable. Assert active rejection and unchanged foreign state rather
than only checking that happy-path queries happen to omit B.

## Adoption gates

Before implementation: approve tenant identity/membership and revocation rules,
action/resource authorization, RLS/runtime roles, canonical format and epochs,
cursor cryptography, export trust contract, legacy evidence handling, leakage
test criteria, and operational owners. Require independent security review and
negative-test evidence before any multi-tenant isolation claim.

**IMPLEMENTED: NO — DESIGN ONLY.** This delivery creates only this document.
