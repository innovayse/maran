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

    /// <summary>
    /// The one hosted service this module carries is the backup reclaimer, and it is exactly one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test replaces one that asserted the GAP.</b> Until the reclaimer landed, the check
    /// here was <c>No_type_in_this_module_reclaims_a_backup_the_panel_lost_track_of</c>: it asserted
    /// that NOTHING in this assembly implemented <c>IHostedService</c>, so that the doc-comment notes
    /// describing the unreclaimed backup could not outlive the gap. The gap is closed, so that
    /// assertion is gone and this one stands in its place — by name, so that deleting
    /// <c>StartupBackupReconciler</c> or quietly renaming it is as red as leaving the gap was.
    /// </para>
    /// <para>
    /// <b>Both directions are asserted deliberately.</b> The name pins that the reclaimer is here;
    /// the count of one pins that a SECOND unattended pass has not appeared in this module without
    /// somebody saying so — a hosted service is work that runs with no caller behind it, and this is
    /// the assembly whose unattended work deletes and fails customer records.
    /// </para>
    /// <para>
    /// <b>The registration is NOT asserted here and cannot be.</b> A module may not register its own
    /// hosted service (the schedule is Host composition), so the line that makes this class actually
    /// run lives in <c>Maran.Host/Extensions/BackgroundWorkExtensions.cs</c> and is asserted by the
    /// Host's own tests. That split is why an unregistered reclaimer would still pass every test in
    /// THIS project, and why this remark says so rather than leaving the next reader to assume
    /// otherwise.
    /// </para>
    /// <para>
    /// It is asserted by full type name rather than by referencing the hosting package, so the test
    /// project gains no dependency in order to observe the shape.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_only_hosted_service_in_this_module_is_the_backup_reclaimer()
    {
        var hosted = Array.FindAll(typeof(BackupsModule).Assembly.GetTypes(), type =>
        {
            return Array.Exists(type.GetInterfaces(), contract =>
            {
                return contract.FullName == "Microsoft.Extensions.Hosting.IHostedService";
            });
        });

        Assert.Equal([typeof(Maran.Modules.Backups.Services.StartupBackupReconciler)], hosted);
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
