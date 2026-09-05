using System.Resources;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring;

/// <summary>
/// The Monitoring module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="MonitoringDbContext"/> against the <c>monitoring</c> PostgreSQL schema and contributes
/// the module's controllers to the Host's routing. Owns what the panel knows about the machine it
/// runs on — the samples behind the charts and the alert state machine (spec §11).
/// </summary>
/// <remarks>
/// Mail is not this module's. An alert reaches an operator the way every module's mail does — by
/// publishing <see cref="SendMailRequested"/> for the Notifications module to send — so nothing
/// else in the panel depends on this module being loaded in order to send.
/// </remarks>
public sealed class MonitoringModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Monitoring.Resources.DisplayNames";

    /// <inheritdoc />
    public string Name
    {
        get
        {
            return Manifest.Id;
        }
    }

    /// <inheritdoc />
    public Manifest Manifest
    {
        get
        {
            return MonitoringManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped, like every module context. This one carries no tenant filter — every row describes
        // the server rather than a customer — but a DbContext is not thread-safe and a singleton one
        // would be shared by every concurrent request, and by the sampler's own timer besides.
        services.AddDbContext<MonitoringDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor — and
        // records the panel itself as the actor when there is no request, which is most of the time
        // in this module.
        services.AddScoped<MonitoringAuditJournal>();
        services.AddScoped<AlertEvaluator>();

        // Registered rather than left for the message bus to construct, so the nightly retention pass
        // is resolvable — and therefore drivable by a test — exactly as the Tasks module's equivalent
        // is. A handler only the bus can build is a handler only a booted bus can exercise.
        services.AddScoped<Jobs.SampleRetentionHandler>();

        // MetricsSampler is deliberately NOT registered here. It is a hosted service, and a module
        // may not register one: the schedule is Host composition (see BackgroundWorkExtensions),
        // which is also the right split — how often a server is sampled is a deployment decision,
        // not the module's behaviour. The same shape StartupBanReconciler uses.

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves
        // Manifest.DisplayNameKey against. No ErrorMessages pool: this module returns no error codes
        // of its own. Module-internal lookups inject IStringLocalizer<T> instead.
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(MonitoringModule).Assembly));
    }
}
