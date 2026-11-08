using System.Resources;
using Maran.Modules.Sites.Interfaces;
using Maran.Modules.Sites.Jobs;
using Maran.Modules.Sites.Options;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites;

/// <summary>
/// The Sites module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="SitesDbContext"/> against the <c>sites</c> PostgreSQL schema and contributes the
/// module's controllers to the Host's routing. Owns websites: the domain, its aliases, the backend
/// that renders it, and whether it serves (spec §11).
/// </summary>
public sealed class SitesModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Sites.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Sites.Resources.DisplayNames";

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
            return SitesManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default, which is what the tenant filter requires: the context closes over the
        // request's own ICurrentUser, so a singleton context would freeze one caller's tenant scope
        // and serve it to everybody.
        services.AddDbContext<SitesDbContext>(options =>
        {
            // Split, because a site loads its options with it and the options load three collections:
            // one joined query would multiply their rows (redirects x users x addresses) for every site.
            options.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, SitesModuleMigrations>();

        // Scoped, because it reads the request's own tenant-scoped DbContext: this module's answer to the
        // personal-data export, an allow-list of fields and never a secret.
        services.AddScoped<IPersonalDataProvider, SitesPersonalDataProvider>();

        // Scoped, because it reads the request's own tenant-scoped DbContext, ICurrentUser and the two
        // feature windows: this module's answer to the global search, sites found by any hostname.
        services.AddScoped<ISearchProvider, SitesSearchProvider>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against; see AccountsModule for the full reasoning. Module-internal
        // lookups inject IStringLocalizer<T> directly instead.
        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<SiteAuditJournal>();

        // Scoped, because the localizer it wraps resolves the request's own culture.
        services.AddScoped<SiteDisplayNames>();

        // Scoped, because each takes the request's own DbContext or agent client: the settings and
        // versions of an account's pools for every writer of a pool, the writer that rewrites them
        // across versions with compensation, and the view of the settings a screen reads (issue #138).
        services.AddScoped<PhpAccountPoolSettings>();
        services.AddScoped<PhpPoolApplier>();
        services.AddScoped<PhpSettingsReader>();

        // Scoped, because it reads the request's own tenant-filtered context and caller: the installed
        // PHP versions with their support life, and for an administrator the sites using each.
        services.AddScoped<PhpVersionListing>();

        // Scoped, because it takes a transaction on the request's own DbContext connection: the
        // advisory lock it holds is scoped to that transaction and must be released by its commit.
        services.AddScoped<ISiteSlotGate, SiteSlotGate>();

        // Scoped, because it reads through the request-scoped context, deliberately without the tenant
        // filter: a hostname is claimed once across the whole server.
        services.AddScoped<SiteHostnameClaims>();

        // Scoped, because it asks the claims reader above and drives the agent on the request's behalf.
        services.AddScoped<SiteVhostCompensator>();

        // Scoped, because it takes the request's slot gate and compensator.
        services.AddScoped<SiteRecorder>();

        // Scoped, because it invokes the bus on behalf of the request that created the site, so a subscriber
        // reads as that caller.
        services.AddScoped<SiteCreationAnnouncer>();

        // The stream's settings, validated at startup like every other options class.
        services.AddOptions<SiteLogOptions>()
            .Bind(configuration.GetSection(SiteLogOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton: it holds no per-request state, and formats frames into a response it is handed.
        services.AddSingleton<SiteLogStreamWriter>();

        // The one window other modules have onto the sites table, and the one hand on
        // Site.HasCertificate (see ISiteDirectory). Scoped, because it reads and writes through the
        // request-scoped, tenant-filtered context.
        services.AddScoped<ISiteDirectory, SiteDirectory>();

        // The narrow window the DNS module asks whether an account's site serves a host name through (issue
        // #178). Scoped, because it reads through the request's tenant-filtered context.
        services.AddScoped<ISiteHostnameDirectory, SiteHostnameDirectory>();

        // This module's answer to how many sites an account owns, for the Accounts module's resource
        // summary. Scoped, because it reads through the request-scoped, tenant-filtered context.
        services.AddScoped<IAccountResourceCounter, SiteAccountResourceCounter>();

        // Scoped, because it reads through the request-scoped, tenant-filtered context and journals
        // as the request's own caller. Resolved by SitesController, which is itself scoped.
        services.AddScoped<SiteLogTailService>();

        // Singleton: it holds the one permit that admits a single site's log maintenance at a time, which
        // must be the same permit for the hourly pass and for the message a retention change publishes.
        services.AddSingleton<SiteLogMaintenanceGate>();

        // Scoped, because each reads and writes through the request-scoped context (a background pass
        // opens its own scope) and drives the agent: the traffic read, the archive prune, the expiry of
        // old rows, and the per-site order of the first two.
        services.AddScoped<SiteTrafficIngestor>();
        services.AddScoped<SiteLogPruner>();
        services.AddScoped<SiteTrafficPurger>();
        services.AddScoped<SiteLogMaintainer>();

        // Scoped, because it writes through the request-scoped context — resolved once per Wolverine message,
        // the same way the Tasks module resolves its own retention handler. The Host schedules
        // SiteLogMaintenanceRequested every hour.
        services.AddScoped<SiteLogMaintenanceHandler>();

        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(SitesModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(SitesModule).Assembly));
    }
}
