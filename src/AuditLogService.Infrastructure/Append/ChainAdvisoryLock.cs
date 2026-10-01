namespace AuditLogService.Infrastructure.Append;

/// <summary>
/// The PostgreSQL transaction-scoped advisory lock used to serialize appends to the
/// global audit event hash chain.
/// </summary>
/// <remarks>
/// <para>
/// plan.md selects a single, globally ordered hash chain guarded by a PostgreSQL
/// transaction-scoped advisory lock (<c>pg_advisory_xact_lock</c>) as the concurrency
/// control mechanism for appends. The lock is acquired inside the append transaction and
/// is released automatically by PostgreSQL on <c>COMMIT</c> or <c>ROLLBACK</c> - it
/// cannot be leaked by a crashed or disconnected client holding it indefinitely.
/// </para>
/// <para>
/// <see cref="Key"/> is an arbitrary, fixed 64-bit constant. It identifies "the audit
/// event chain" as a lock domain and is unrelated to <see cref="Persistence.ChainMetadata.GlobalChainId"/>
/// (the chain-metadata row's primary key, which is a normal data value, not a lock
/// identifier). Using a separate constant avoids any accidental collision between this
/// lock and lock keys any future feature might introduce. The value itself carries no
/// meaning beyond being fixed and unique to this lock's purpose; it must never be
/// generated at runtime, because advisory locks only serialize callers that request the
/// exact same key.
/// </para>
/// <para>
/// See docs/concurrency.md for the full design rationale and trade-offs.
/// </para>
/// </remarks>
public static class ChainAdvisoryLock
{
    /// <summary>
    /// The fixed advisory lock key guarding appends to the global audit event chain.
    /// </summary>
    public const long Key = 713_204_890_501L;
}
