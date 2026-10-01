// Disables all test parallelization for this assembly. Every integration test here shares the
// same physical PostgreSQL database and resets it via PostgreSqlFixture.ResetAsync(); xUnit's
// default collection-based parallelization only guarantees sequencing *within* a collection, so
// without this assembly-wide override, test classes could still run concurrently against the
// same database and corrupt each other's state (duplicate sequence numbers, leaked rows from a
// concurrently-running test's inserts, etc.).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
