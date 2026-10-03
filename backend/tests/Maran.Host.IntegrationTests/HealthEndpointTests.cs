using System.Net;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Covers the readiness case that needs a real, reachable database — the in-memory
/// <c>Maran.Host.Tests</c> project covers the unreachable/not-configured cases without the
/// cost of a container.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public HealthEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a host against this test's own database, with the settings startup validation refuses
    /// to boot without.
    /// </summary>
    /// <remarks>
    /// Extracted so that the ready/not-ready pair below share one host definition: two copies would
    /// be two places for the settings to drift, and a readiness test built differently from the one
    /// it is the control for proves nothing about either.
    /// </remarks>
    /// <returns>A factory the caller disposes.</returns>
    private WebApplicationFactory<Program> NewFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Testing, not Development: inheriting the developer's database settings made these
            // tests pass locally against the wrong database and fail in CI.
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
            builder.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            builder.UseSetting("Jwt:SigningKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>
    /// Applies every registered module's migrations to this test's database, the way step 65 does on
    /// a server.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <returns>Resolves when the schema exists.</returns>
    private static async Task MigrateAsync(IServiceProvider services)
    {
        foreach (var contextType in services.GetServices<DbContextOptions>()
                     .Select(options => { return options.ContextType; })
                     .Distinct())
        {
            await using var scope = services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await context.Database.MigrateAsync();
        }
    }

    /// <summary>Readiness endpoint returns 200 when the database is reachable and migrated.</summary>
    /// <returns>Resolves when the assertion has been made.</returns>
    [Fact]
    public async Task Readiness_endpoint_returns_200_when_the_database_is_reachable()
    {
        await using var factory = NewFactory();
        using var client = factory.CreateClient();

        // The schema FIRST, because readiness now means "this panel can serve", not "a connection
        // opened". An empty database is covered by its own test below; mixing the two would leave
        // neither proved.
        await MigrateAsync(factory.Services);

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A database that answers but holds no schema is NOT ready, and this is the inverse control for
    /// the test above: without it, both would pass for a readiness check that never looked at the
    /// database at all.
    /// </summary>
    /// <remarks>
    /// This is the state that went undetected through three releases. Nothing applied the
    /// migrations, every fresh install came up empty, and the probe reported the database as
    /// "reachable" because it had opened a connection and asked nothing further — so `/health` said
    /// the panel was fine while the first thing an operator did answered
    /// `relation "identity.Users" does not exist` (issues #66, #68).
    /// </remarks>
    /// <returns>Resolves when the assertion has been made.</returns>
    [Fact]
    public async Task Readiness_endpoint_refuses_when_the_database_has_no_schema()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        // Deliberately NOT migrated: this is a freshly created database, exactly as the installer
        // leaves it before step 65 runs.
        var response = await client.GetAsync("/health/ready");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
