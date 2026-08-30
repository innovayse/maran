using System.Resources;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity;

/// <summary>
/// The Identity module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="IdentityDbContext"/> against the <c>identity</c> PostgreSQL schema and contributes
/// the module's controllers to the Host's routing. Owns who may log into the panel: users, their
/// roles, their sessions, their second factor, and the append-only audit journal (spec §10).
/// </summary>
public sealed class IdentityModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Identity.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Identity.Resources.DisplayNames";

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
            return IdentityManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IAuditWriter, DatabaseAuditWriter>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITotpService, TotpService>();
        services.AddScoped<IRecoveryCodeService, RecoveryCodeService>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against; see AccountsModule for the full reasoning. Module-internal
        // lookups inject IStringLocalizer<T> directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(IdentityModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(IdentityModule).Assembly));
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Controllers are discovered by ASP.NET Core's controller model (Program.cs calls
        // MapControllers() once for the whole app) — this module has no endpoints to map by hand.
    }
}
