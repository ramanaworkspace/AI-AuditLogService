# Append concurrency: global advisory-lock design

## Purpose

The audit log is a single, globally ordered hash chain (see
`docs/canonical-hashing.md`). Every append must read the current chain head,
compute the next record from it, and advance the head to that record, with no
other append interleaved in between. Without a correctness mechanism, two
concurrent append requests could both read the same head, both compute a
record with the same `sequenceNumber` and the same `previousHash`, and both
insert - forking the chain or creating duplicate sequence numbers. plan.md
selects a **global PostgreSQL transaction-scoped advisory lock**
(`pg_advisory_xact_lock`) as the mechanism that prevents this.

## Why a database-level lock, not an in-memory lock

An in-process lock (for example a .NET `SemaphoreSlim` or `lock` statement)
only serializes threads inside one process. As soon as the service runs as
more than one instance - multiple containers, multiple replicas, a rolling
deployment with the old and new instance briefly both running - an in-memory
lock stops providing any guarantee at all, because each process has its own,
independent lock. The correctness of "exactly one append in flight against the
chain head at a time" must therefore be enforced by something every instance
shares: the database itself. `pg_advisory_xact_lock` is PostgreSQL's
server-side advisory lock, held by the database session for the lifetime of
the current transaction, so every process that opens a connection to the same
database competes for the same lock regardless of which process it is.

## The lock

`ChainAdvisoryLock.Key` (`src/AuditLogService.Infrastructure/Append/ChainAdvisoryLock.cs`)
is a fixed `long` constant (`713_204_890_501`). It is:

- **Fixed**, not generated at runtime - advisory locks only serialize callers
  requesting the *exact same* key, so a random or per-call key would provide
  no mutual exclusion at all.
- **Unrelated to any data value**, in particular to
  `ChainMetadata.GlobalChainId` (`= 1`, the chain-metadata row's primary key).
  The lock key identifies "the audit event chain" as a lock domain; the chain
  ID identifies a row. Keeping them in separate numeric domains avoids an
  accidental collision if the lock key space is ever extended (for example,
  per-chain locks keyed by chain ID) in the future.
- **Transaction-scoped** (`pg_advisory_xact_lock`, not the session-scoped
  `pg_advisory_lock`). PostgreSQL releases a transaction-scoped advisory lock
  automatically on `COMMIT` or `ROLLBACK`. This means the lock can never be
  leaked: a crashed process, a dropped connection, or an exception thrown
  partway through the append all end the transaction and release the lock,
  with no explicit "unlock" code path required and no risk of a stuck lock
  requiring manual intervention.

## The atomic append flow

`PostgresAuditEventAppendService.AppendAsync`
(`src/AuditLogService.Infrastructure/Append/PostgresAuditEventAppendService.cs`)
performs exactly one database transaction per append, in this order:

1. **Begin the transaction.**
2. **Acquire `pg_advisory_xact_lock(ChainAdvisoryLock.Key)`.** This blocks
   until no other append transaction holds the lock, so only one transaction
   at a time can proceed past this point.
3. **Read the current chain head** (`ChainMetadata` row, `ChainId = 1`):
   `HeadSequenceNumber` and `HeadHash`.
4. **Determine the next `SequenceNumber`** (`HeadSequenceNumber + 1`).
5. **Determine `PreviousHash`** (`HeadHash` - the previous record's
   `ContentHash`, or the `GENESIS` literal if the chain is empty).
6. **Construct the immutable event.** `EventId` and `Timestamp` are
   server-assigned here (`Guid.NewGuid()`, `DateTimeOffset.UtcNow`); clients
   never supply them and cannot control chain order.
7. **Canonically serialize the event and compute its `ContentHash`**
   (`AuditEvent.Create`, using `ICanonicalEventSerializer` /
   `IEventHasher` - see `docs/canonical-hashing.md`).
8. **Insert the event row.**
9. **Update the chain-head row** (`HeadSequenceNumber`, `HeadHash`) to point at
   the newly inserted event, in the same `SaveChangesAsync` round trip as the
   insert.
10. **Commit the transaction** (and, with it, release the advisory lock
    acquired in step 2).

If any step throws, the transaction is never committed. Disposing it (the
service uses `await using var transaction = ...`) rolls it back automatically,
so the event insert and the chain-head update are discarded together - there
is no state in which an event is persisted but the chain head was not
advanced, or vice versa. Because the advisory lock serializes every append
against the same chain, the sequence number and previous hash read in steps
3-5 are still correct at commit time: no other transaction can have read the
same head and committed a conflicting record in between, since it would have
been blocked at step 2 until this transaction finished.

This is reinforced, not replaced, by database constraints: `AuditEvents` has
unique constraints on `EventId` and `SequenceNumber` (see
`AuditEventRecordConfiguration`), so even a hypothetical bug that bypassed the
advisory lock would still be rejected by the database at insert time rather
than silently corrupting the chain.

## Trade-off

This design deliberately chooses **strongest and simplest correctness over
write throughput**:

- **Strongest / simplest ordering.** A single lock around the single chain
  head means there is exactly one write path and exactly one place where
  ordering is decided. There is no distributed consensus, no optimistic-retry
  loop, and no partitioning scheme to reason about.
- **Simpler verification.** Because every event is appended under the same
  lock in strict sequence, verifying the chain later is a single linear scan:
  each record's `PreviousHash` must equal its predecessor's `ContentHash`, with
  no need to reconcile concurrent branches or merge interleaved partitions.
- **Single global write bottleneck.** Every append across the entire service,
  regardless of actor, resource, or event type, serializes through the same
  lock and the same chain-head row. Append throughput is bounded by how fast
  one transaction can run steps 2-10, not by how much hardware is added.
- **Horizontal write scalability is intentionally sacrificed.** Adding more
  service instances or more database capacity does not increase append
  throughput, because all instances still contend for the same single
  advisory lock key and the same single chain-head row. This is an accepted,
  deliberate consequence of requiring one globally ordered chain rather than,
  for example, multiple independently ordered chains (one per resource or
  actor) that would scale horizontally but would not provide a single,
  simple, global ordering guarantee.

Read access (retrieving events, verifying the chain) is not serialized by this
lock and is unaffected by this trade-off; only the append path is a global
bottleneck.

## Limitations

The advisory lock guarantees ordering and atomicity of appends; it does not,
by itself, make the chain tamper-evident - that guarantee comes from the
canonical hashing described in `docs/canonical-hashing.md`. It also does not
address retention, redaction, export, compliance reporting, or the HTTP API
surface; those are explicitly out of scope for this change.
