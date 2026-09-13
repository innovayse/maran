using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Jobs;
using Maran.Modules.Monitoring.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What one nightly retention pass over <c>monitoring.Samples</c> removes, and what it must not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an integration test.</b> <c>SampleRetentionHandler</c> deletes with
/// <c>ExecuteDeleteAsync</c>, which is translated by the database provider and is not implemented by
/// the in-memory provider the module's unit tests use. A unit test could therefore only assert
/// against a handler that cannot run — the shape of test that agrees with any implementation.
/// </para>
/// <para>
/// <b>Seven days, and that is R10's whole storage design.</b> Raw 60-second samples for a week is
/// about 10,080 rows, bucketed on read by <c>date_bin</c>; there is no rollup table, so this one
/// delete is the only thing standing between the panel and a table that grows a row a minute for
/// ever.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MetricsRetentionTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>
    /// How many rows the multi-batch test seeds. Larger than the handler's own batch size (1,000) so
    /// the pass has to loop; if that constant is ever raised past this number the test stops covering
    /// the loop and this comment is where to start.
    /// </summary>
    private const int MoreThanOneBatch = 1_200;

    /// <summary>Comfortably outside the seven-day window, whatever hour the suite runs at.</summary>
    private static readonly TimeSpan WellOutsideTheWindow = TimeSpan.FromDays(30);

    /// <summary>Comfortably inside the seven-day window.</summary>
    private static readonly TimeSpan WellInsideTheWindow = TimeSpan.FromHours(6);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MetricsRetentionTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A pass removes samples older than the window and keeps every recent one.</summary>
    [Fact]
    public async Task A_pass_removes_samples_older_than_the_window_and_keeps_recent_ones()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var monitoring = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        monitoring.Samples.Add(Sample(now - WellOutsideTheWindow));
        monitoring.Samples.Add(Sample(now - WellInsideTheWindow));
        await monitoring.SaveChangesAsync();

        var purged = await scope.ServiceProvider.GetRequiredService<SampleRetentionHandler>()
            .HandleAsync(new SampleRetentionRequested(), CancellationToken.None);

        Assert.Equal(1, purged);
        var remaining = await monitoring.Samples.AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.True(remaining[0].CapturedAt > now - SampleRetentionHandler.RetentionWindow);
    }

    /// <summary>More rows than one batch are removed by looping, not by one enormous statement.</summary>
    /// <remarks>
    /// A server that ran a year without this handler, then upgraded, finds hundreds of thousands of
    /// eligible rows on its first pass. One <c>DELETE</c> covering all of them holds its locks and its
    /// WAL growth for as long as that statement runs, on the table the sampler writes to every minute.
    /// </remarks>
    [Fact]
    public async Task More_rows_than_one_batch_are_removed_by_looping()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var monitoring = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        for (var row = 0; row < MoreThanOneBatch; row++)
        {
            monitoring.Samples.Add(Sample(now - WellOutsideTheWindow - TimeSpan.FromMinutes(row)));
        }

        await monitoring.SaveChangesAsync();

        var purged = await scope.ServiceProvider.GetRequiredService<SampleRetentionHandler>()
            .HandleAsync(new SampleRetentionRequested(), CancellationToken.None);

        Assert.Equal(MoreThanOneBatch, purged);
        Assert.Empty(await monitoring.Samples.AsNoTracking().ToListAsync());
    }

    /// <summary>A pass over a caught-up table removes nothing and says so.</summary>
    [Fact]
    public async Task A_pass_over_a_caught_up_table_removes_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var monitoring = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        monitoring.Samples.Add(Sample(now - WellInsideTheWindow));
        await monitoring.SaveChangesAsync();

        var purged = await scope.ServiceProvider.GetRequiredService<SampleRetentionHandler>()
            .HandleAsync(new SampleRetentionRequested(), CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.Single(await monitoring.Samples.AsNoTracking().ToListAsync());
    }

    /// <summary>Builds one sample row at a given instant.</summary>
    /// <param name="capturedAt">When the reading was taken.</param>
    /// <returns>The sample.</returns>
    private static MetricSample Sample(DateTimeOffset capturedAt)
    {
        return new MetricSample(capturedAt, 1, 1, 2, 1, 2, 1, 2, 0.1);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMonitorClient>(new StubAgentMonitorClient());
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
    }
}
