namespace AuditLogService.IntegrationTests.Persistence;

/// <summary>
/// Groups every test class that exercises the shared PostgreSQL test database into a single
/// xUnit collection. Tests within a collection run sequentially (never in parallel), which is
/// required here because all of these tests share one physical database/connection string and
/// call <see cref="PostgreSqlFixture.ResetAsync"/> to reset shared state (the audit_events table
/// and chain-head metadata). Running them in parallel would let one test's reset/append race with
/// another test's assertions, producing spurious failures such as duplicate sequence numbers or
/// duplicate predecessors that are an artifact of test interference rather than the append
/// operation itself.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL database";
}
