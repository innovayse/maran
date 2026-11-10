using System.Resources;
using Maran.Modules.Apps.Interfaces;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Apps;

/// <summary>
/// The Apps module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="AppsDbContext"/> against the <c>apps</c> PostgreSQL schema and contributes the module's
/// controllers to the Host's routing. Owns the app store (issue #517): which web server and which
/// language runtimes the server carries for its sites, installed and removed as recorded tasks through
/// the agent, and the first-run gate that keeps sites from being created before a web server exists.
/// </summary>
public sealed class AppsModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Apps.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Apps.Resources.DisplayNames";

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
            return AppsManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped, like every module context. This one carries no tenant filter: the apps on a server
        // belong to the server, not to a customer.
        services.AddDbContext<AppsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Singleton and stateless: it opens a scope of its own per call, so the Host's migrate verb can
        // bring this module's schema up to date without knowing AppsDbContext.
        services.AddSingleton<IModuleMigrations, AppsModuleMigrations>();

        // Scoped, because the journal reads the request's own ICurrentUser for the actor.
        services.AddScoped<AppsAuditJournal>();
        services.AddScoped<AppDisplayNames>();

        // The window other modules ask what the stack has installed through (rules/architecture.md "A
        // shared facility's contract lives in Maran.Sdk"). The Sites module asks it before it creates or
        // changes a site, which is where the first-run gate is enforced.
        services.AddScoped<IStackStatus, StackStatus>();

        // The install/uninstall job and the seam that hands an accepted operation to the bus.
        services.AddScoped<AppOperationRunner>();
        services.AddScoped<IAppOperationDispatcher, MessageBusAppOperationDispatcher>();
        services.AddScoped<AppUsageGuard>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(AppsModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(AppsModule).Assembly));
    }
}
