using Maran.Agent.Simulated.Interfaces;
using Maran.Host.Cli.Interfaces;
using Maran.Host.Demo.Cli;
using Maran.Host.Demo.Reset;
using Maran.Host.Demo.Seeding;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.Seeding.Steps;
using Maran.Host.Demo.World;
using Maran.Host.Security;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.Demo.Extensions;

/// <summary>
/// Registers the demonstration's world: its settings, seed, reset marker, status and command-line verbs.
/// </summary>
public static class DemoWorldExtensions
{
    /// <summary>
    /// The configuration key that says whether the simulated server starts out holding the world. It is on
    /// for a host that serves visitors, and off for the one that runs <c>demo-seed</c>: that run creates the
    /// world through the real commands, and a server that already held it would answer every create with
    /// <c>AgentAlreadyExists</c>.
    /// </summary>
    public const string SimulatedWorldSetting = "Demo:SimulatedWorld";

    /// <summary>
    /// Adds the world. Must run after the Host's own security registration, because it replaces the
    /// current-user service with one that also honours the caller the seed acts as.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The configuration the settings are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDemoWorld(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoIdentityOptions>()
            .Bind(configuration.GetSection(DemoIdentityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DemoResetOptions>()
            .Bind(configuration.GetSection(DemoResetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<DemoSeedCallerAccessor>();
        services.AddScoped<HttpContextCurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, DemoSeedAwareCurrentUser>());

        if (configuration.GetValue(SimulatedWorldSetting, true))
        {
            services.AddSingleton<ISimulatedAgentSeed, DemoWorldAgentSeed>();
            services.AddSingleton<ISimulatedAgentSeed, DemoWorldHostingSeed>();
        }

        services.AddSingleton<DemoResetMarker>();
        services.AddScoped<DemoStatusProvider>();

        services.AddScoped<DemoSeedCommandRunner>();
        services.AddScoped<DemoInvitationAcceptor>();
        services.AddSingleton<DemoProtectedResourceLoader>();
        services.AddHostedService<DemoProtectedResourceStartup>();
        services.AddSingleton<DemoSeeder>();
        services.AddSingleton<DemoSeedVerifier>();
        services.AddSingleton<DemoMessagingLifetime>();

        // Registration order is run order: later steps need the ids earlier ones produce.
        services.AddScoped<IDemoSeedStep, ReferenceDataSeedStep>();
        services.AddScoped<IDemoSeedStep, AdministratorSeedStep>();
        services.AddScoped<IDemoSeedStep, SecurityPolicySeedStep>();
        services.AddScoped<IDemoSeedStep, PlanSeedStep>();
        services.AddScoped<IDemoSeedStep, ResellerSeedStep>();
        services.AddScoped<IDemoSeedStep, ResellerPlanSeedStep>();
        services.AddScoped<IDemoSeedStep, AccountSeedStep>();
        services.AddScoped<IDemoSeedStep, AppStackSeedStep>();
        services.AddScoped<IDemoSeedStep, SiteSeedStep>();
        services.AddScoped<IDemoSeedStep, CertificateSeedStep>();
        services.AddScoped<IDemoSeedStep, DatabaseSeedStep>();
        services.AddScoped<IDemoSeedStep, FileTransferSeedStep>();
        services.AddScoped<IDemoSeedStep, CronSeedStep>();
        services.AddScoped<IDemoSeedStep, BackupSeedStep>();
        services.AddScoped<IDemoSeedStep, FirewallSeedStep>();
        services.AddScoped<IDemoSeedStep, MonitoringSeedStep>();
        services.AddScoped<IDemoSeedStep, AccountStateSeedStep>();

        services.AddSingleton<ICliCommand, DemoSeedCliCommand>();
        services.AddSingleton<ICliCommand, DemoResetCliCommand>();

        return services;
    }
}
