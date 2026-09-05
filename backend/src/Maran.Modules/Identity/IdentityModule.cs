using System.Resources;
using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Authorization;

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

        // ValidateOnStart, like every options class the panel binds (rules/csharp.md "Options
        // validated at startup"). A threshold of zero would ban the first person to mistype a
        // password and a window of zero would ban nobody ever; both must stop the boot rather than
        // surface as a protection that behaves nothing like its documentation.
        services.AddOptions<BruteForceOptions>()
            .Bind(configuration.GetSection(BruteForceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The panel's own public address, for the link in a password-reset mail. Never taken from
        // the request's Host header, which the caller controls — see PasswordResetOptions.
        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection(PasswordResetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IAuditWriter, DatabaseAuditWriter>();
        services.AddScoped<IdentityAuditJournal>();

        // Singleton: it holds one row for the life of the process and is read on the sign-in path,
        // the token-issuing path and every password validator. It resolves its scoped DbContext
        // through a scope factory rather than capturing one — see SecurityPolicyCache.
        services.AddSingleton<SecurityPolicyCache>();

        // The forced-two-factor steering. The requirement is attached to the panel's authorization
        // policies by the Host (RolePolicies); the handler that decides it lives here, with the
        // module that issues the claim it reads.
        services.AddSingleton<IAuthorizationHandler, TwoFactorEnrolmentCompleteHandler>();

        // Scoped, because it counts through the request's own DbContext and saves alongside the
        // audit entry for the same refused attempt.
        services.AddScoped<BruteForceDetector>();
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
}
