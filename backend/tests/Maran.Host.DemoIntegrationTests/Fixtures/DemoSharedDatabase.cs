namespace Maran.Host.DemoIntegrationTests.Fixtures;

/// <summary>Names the collection whose tests share one PostgreSQL server.</summary>
[CollectionDefinition(Name)]
public sealed class DemoSharedDatabase : ICollectionFixture<DemoPostgres>
{
    /// <summary>The collection's name, as the test classes spell it.</summary>
    public const string Name = "demo";
}
