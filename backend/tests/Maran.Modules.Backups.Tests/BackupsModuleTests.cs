using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Backups.Tests;

/// <summary>
/// What the module's own registrations promise, and what its published identity claims about the
/// parts of the agent it drives.
/// </summary>
/// <remarks>
/// The module used to bind <c>Backups__LocalRoot</c> and refuse the boot on an unacceptable
/// spelling of it. Both are gone: the agent writes under its own constant and refuses a local
/// destination that carries a path, so the setting could only change what the panel SAID, and
/// nothing checked that statement against the agent's. The tests that pinned the setting are gone
/// with it — a test for a removed configuration key is the last place such a key survives.
/// </remarks>
public sealed class BackupsModuleTests
{
    /// <summary>The module registers nothing that binds a configured backup directory.</summary>
    /// <remarks>
    /// The measurable form of "the operator cannot be given a false statement of where backups
    /// are": there is no options type left for a value to reach. Asserted through the module's own
    /// registrations rather than by reading source, so re-introducing the setting fails here.
    /// </remarks>
    [Fact]
    public void The_module_binds_no_backup_directory_setting()
    {
        using var provider = Provider();

        Assert.DoesNotContain(typeof(BackupsModule).Assembly.GetTypes(), type =>
        {
            return type.Namespace is not null
                && type.Namespace.EndsWith(".Options", StringComparison.Ordinal);
        });
    }

    /// <summary>The manifest names both agent areas this module reaches for, and no others.</summary>
    /// <remarks>
    /// The architecture tests check that every capability a module reaches is declared; this checks
    /// the other direction, that the declaration is the backup area plus the identity handshake the
    /// destinations screen reads the backup directory from — and not a wider one.
    /// </remarks>
    [Fact]
    public void The_manifest_declares_the_backup_and_system_capabilities()
    {
        Assert.Equal(
            [Sdk.Contracts.AgentCapability.Backup, Sdk.Contracts.AgentCapability.System],
            BackupsManifest.Instance.AgentCapabilities);
    }

    /// <summary>Builds a provider from the module's own registrations.</summary>
    /// <returns>The built provider.</returns>
    private static ServiceProvider Provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=maran;Username=panel",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        new BackupsModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
