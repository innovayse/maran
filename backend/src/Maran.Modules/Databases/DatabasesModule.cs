using System.Resources;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases;

/// <summary>
/// The Databases module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="DatabasesDbContext"/> against the <c>databases</c> PostgreSQL schema and contributes
/// the module's controllers to the Host's routing. Owns customer MySQL databases: which account
/// asked for one, what MySQL calls it, and which dedicated user goes with it (spec §11).
/// </summary>
public sealed class DatabasesModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Databases.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Databases.Resources.DisplayNames";

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
            return DatabasesManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default, which is what the tenant filter requires: the context closes over the
        // request's own ICurrentUser, so a singleton context would freeze one caller's tenant scope
        // and serve it to everybody.
        services.AddDbContext<DatabasesDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<DatabaseAuditJournal>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(DatabasesModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(DatabasesModule).Assembly));
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Controllers are discovered by ASP.NET Core's controller model (Program.cs calls
        // MapControllers() once for the whole app) — this module has no endpoints to map by hand.
    }
}
