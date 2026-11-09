using System.Resources;
using Maran.Modules.Fleet.Persistence;
using Maran.Modules.Fleet.Seeders;
using Maran.Modules.Fleet.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Fleet;

/// <summary>
/// The Fleet module's entry point (rules/csharp.md "Canonical backend layout"). Owns the registry of
/// servers the panel manages: the server it runs on, always, and the nodes an administrator enrols
/// (issues #180, #319).
/// </summary>
/// <remarks>
/// This first cut registers the module's context, its migrations and the seeder of the local server.
/// It has no controller and no handler: the enrolment, approval and listing operations arrive in the
/// next change, and nothing dials a node until the endpoint source they bring exists. The module
/// reaches no other module; the rest of the panel will reach it through Sdk windows.
/// </remarks>
public sealed class FleetModule : IPanelModule
{
    /// <summary>The connection string every module context reads.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Fleet.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Fleet.Resources.DisplayNames";

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
            return FleetManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped, like every module context: a singleton DbContext is shared mutable state across
        // concurrent requests whatever it holds.
        services.AddDbContext<FleetDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, FleetModuleMigrations>();

        // Scoped, because it writes through the request-scoped context; the Host runs it at start-up.
        services.AddScoped<LocalServerSeeder>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(FleetModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(FleetModule).Assembly));
    }
}
