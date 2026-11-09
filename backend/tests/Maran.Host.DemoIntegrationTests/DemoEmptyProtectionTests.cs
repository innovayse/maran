using Maran.Host.DemoIntegrationTests.Fixtures;

namespace Maran.Host.DemoIntegrationTests;

/// <summary>A served demo that finds nothing to protect does not start: an empty set would allow everything it guards.</summary>
[Collection(DemoSharedDatabase.Name)]
public sealed class DemoEmptyProtectionTests
{
    /// <summary>The shared server.</summary>
    private readonly DemoPostgres _postgres;

    /// <summary>Creates the class over the shared server.</summary>
    /// <param name="postgres">The shared server.</param>
    public DemoEmptyProtectionTests(DemoPostgres postgres)
    {
        _postgres = postgres;
    }

    /// <summary>A migrated database with no seeded administrator stops the host at start, naming the failed step.</summary>
    [Fact]
    public async Task A_host_over_a_database_with_no_demonstration_refuses_to_start()
    {
        await using var factory = await SeededDemo.StartMigratedWithoutSeedAsync(_postgres);

        var failure = await Record.ExceptionAsync(() =>
        {
            return Task.Run(() =>
            {
                return factory.CreateClient();
            });
        });

        Assert.NotNull(failure);
        Assert.Contains("NoResourceProtected", failure!.ToString(), StringComparison.Ordinal);
    }
}
