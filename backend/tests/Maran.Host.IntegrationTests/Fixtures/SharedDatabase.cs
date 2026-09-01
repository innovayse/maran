namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The xUnit collection every integration test class joins, so all of them share one PostgreSQL server.
/// </summary>
/// <remarks>
/// xUnit runs the classes of a single collection one after another rather than in parallel. That is
/// a deliberate part of the fix, not a side effect to work around: each test boots a whole panel host
/// and hashes passwords with Argon2id, and running nine such classes at once is what saturated the
/// machine and made an unrelated test fail under load. The suite is faster serial with one container
/// than parallel with ninety.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SharedDatabase : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name test classes name in their <c>[Collection]</c> attribute.</summary>
    public const string Name = "postgres";
}
