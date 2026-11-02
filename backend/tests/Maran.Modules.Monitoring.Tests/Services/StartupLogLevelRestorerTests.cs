using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>The stored log level is applied when the panel starts, and nothing is applied without one.</summary>
public sealed class StartupLogLevelRestorerTests
{
    /// <summary>A stored level is applied at start-up.</summary>
    [Fact]
    public async Task A_stored_level_is_applied_at_start_up()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var seed = MonitoringTestContext.Create(databaseName))
        {
            seed.LogLevelSettings.Add(new LogLevelSetting(PanelLogLevel.Warning, new FakeClock().UtcNow));
            await seed.SaveChangesAsync();
        }

        var levelSwitch = new FakePanelLogLevelSwitch(PanelLogLevel.Information);
        var restorer = CreateRestorer(databaseName, levelSwitch);

        await restorer.StartAsync(CancellationToken.None);

        Assert.Equal([PanelLogLevel.Warning], levelSwitch.Applied);
    }

    /// <summary>Without a stored level the configured one stays in force.</summary>
    [Fact]
    public async Task Without_a_stored_level_the_configured_one_stays_in_force()
    {
        var levelSwitch = new FakePanelLogLevelSwitch(PanelLogLevel.Debug);
        var restorer = CreateRestorer(Guid.NewGuid().ToString(), levelSwitch);

        await restorer.StartAsync(CancellationToken.None);

        Assert.Empty(levelSwitch.Applied);
        Assert.Equal(PanelLogLevel.Debug, levelSwitch.Current);
    }

    /// <summary>Builds the restorer over an in-memory database of the given name.</summary>
    /// <param name="databaseName">The in-memory database the scope's context opens.</param>
    /// <param name="levelSwitch">The switch the restorer applies to.</param>
    /// <returns>The restorer.</returns>
    private static StartupLogLevelRestorer CreateRestorer(string databaseName, FakePanelLogLevelSwitch levelSwitch)
    {
        var services = new ServiceCollection();
        services.AddDbContext<MonitoringDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
        });
        var provider = services.BuildServiceProvider();

        return new StartupLogLevelRestorer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            levelSwitch,
            NullLogger<StartupLogLevelRestorer>.Instance);
    }
}
