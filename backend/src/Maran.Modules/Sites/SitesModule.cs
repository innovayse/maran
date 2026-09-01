using System.Resources;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Common.Options;
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
            options.UseNpgsql(connectionString);
        });

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against; see AccountsModule for the full reasoning. Module-internal
        // lookups inject IStringLocalizer<T> directly instead.
        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<SiteAuditJournal>();

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

        // Scoped, because it reads through the request-scoped, tenant-filtered context and journals
        // as the request's own caller. Resolved by SitesController, which is itself scoped.
        services.AddScoped<SiteLogTailService>();

        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(SitesModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(SitesModule).Assembly));
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Controllers are discovered by ASP.NET Core's controller model (Program.cs calls
        // MapControllers() once for the whole app) — this module has no endpoints to map by hand.
    }
}
