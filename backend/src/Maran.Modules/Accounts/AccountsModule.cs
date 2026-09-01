using System.Resources;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts;

/// <summary>
/// The Accounts module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="AccountsDbContext"/> against the <c>accounts</c> PostgreSQL schema and contributes
/// the module's controllers to the Host's routing. Owns hosting accounts: the unit of ownership,
/// one Linux user, a plan with limits, and a suspension state (spec §8).
/// </summary>
public sealed class AccountsModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Accounts.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Accounts.Resources.DisplayNames";

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
            return AccountsManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        services.AddDbContext<AccountsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // The one window other modules have onto this module's data (rules/architecture.md
        // "Cross-module needs go through Wolverine messages or Sdk abstractions"). Scoped, because
        // it reads the request's own DbContext and applies the request's own tenant scope.
        services.AddScoped<IAccountDirectory, AccountDirectory>();

        // Registers this module's resource managers into the shared pool the panel-wide
        // ResxErrorTextProvider resolves error codes and Manifest.DisplayNameKey against
        // (rules/csharp.md "The backend owns all user-facing message text") — that mechanism is
        // generic across every module (ModulesEndpoint, ApiResultExtensions) and cannot be
        // narrowed to this module's own typed IStringLocalizer<T>. Module-internal lookups (e.g.
        // ListPlansQueryHandler resolving a plan's name) instead inject IStringLocalizer<T>
        // directly (rules/csharp.md "Resources are reached through IStringLocalizer<T>"); this
        // registration exists only for the two SDK-level, code-driven lookups above.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(AccountsModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(AccountsModule).Assembly));
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Controllers are discovered by ASP.NET Core's controller model (Program.cs calls
        // MapControllers() once for the whole app) — this module has no endpoints to map by hand.
    }
}
