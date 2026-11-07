using System.Resources;
using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Seeders;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Validators;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

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

        // The cap on one account's team; a nonsensical value stops the boot (SubUserOptions).
        services.AddOptions<SubUserOptions>()
            .Bind(configuration.GetSection(SubUserOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The panel's own public address, for the link in a password-reset mail and in an invitation
        // mail alike. Never taken from the request's Host header, which the caller controls — see
        // PanelOptions.
        services.AddOptions<PanelOptions>()
            .Bind(configuration.GetSection(PanelOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The check data annotations cannot express: the value must be an https origin (PublicUrlRule),
        // and a panel that starts without one says so in its log.
        services.AddSingleton<IValidateOptions<PanelOptions>, PanelOptionsValidator>();

        // Scoped, like every service here that writes through the request's own context. It is
        // resolved by the first-run setup handler and by the startup seeder that starts the setup
        // token's clock.
        services.AddScoped<SetupTokenWindowKeeper>();
        services.AddScoped<SetupTokenWindowSeeder>();

        services.AddScoped<IAuditWriter, DatabaseAuditWriter>();
        services.AddScoped<IdentityAuditJournal>();
        services.AddScoped<InvitationMailComposer>();
        services.AddScoped<MembershipOfferMailComposer>();
        services.AddScoped<InvitationLinkBuilder>();
        services.AddScoped<InvitationIssuer>();
        services.AddScoped<AccountOwnerFinder>();

        // Sub-users: the owner check every operation asks first, and the Sdk window the plan-feature
        // filter asks to learn whether a person was given a feature. Scoped, because both read the
        // request's own DbContext.
        services.AddScoped<SubUserOwnerGuard>();
        services.AddScoped<ISubUserTeamLock, SubUserTeamLock>();
        services.AddScoped<IApiKeyCreationLock, ApiKeyCreationLock>();
        services.AddScoped<IAccountMemberGrants, AccountMemberGrants>();

        services.AddScoped<PasswordResetLinkBuilder>();
        services.AddScoped<AdminRecoveryIssuer>();
        services.AddScoped<AuditActionDisplayNames>();
        services.AddScoped<AuditOutcomeDisplayNames>();

        // The journal's reading, tamper evidence, retention, erasure and export (issues #145, #146). All
        // scoped, because each works through the request's own DbContext (the maintenance job and the
        // account-deletion handler get a scope of their own, so they share nothing with a request).
        services.AddScoped<AuditEventQuery>();
        services.AddScoped<AuditChainSealer>();
        services.AddScoped<AuditChainVerifier>();
        services.AddScoped<AuditRetentionRunner>();
        services.AddScoped<AuditPseudonymiser>();
        services.AddScoped<AuditExportWriter>();
        services.AddScoped<PersonalDataExportWriter>();
        services.AddScoped<PersonalDataAuditRows>();
        services.AddScoped<IPersonalDataProvider, IdentityPersonalDataProvider>();

        // A singleton, because the guarantee that only one walk along the chain runs at a time is only
        // worth anything if every scope shares the same gate (AuditChainVerificationGate).
        services.AddSingleton<AuditChainVerificationGate>();

        // API keys (issue #144): the scope names the create dialog offers, and the authenticator the
        // Host's ApiKey scheme asks through the Sdk window. Scoped, because the authenticator reads and
        // stamps the request's own DbContext and reads the row on every call (no cache, so a revocation
        // takes effect on the next request).
        services.AddScoped<ApiKeyScopeDisplayNames>();
        services.AddScoped<ApiKeyStatusDisplayNames>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();

        // The per-call audit of key calls: global, and a no-op for every caller that is not an API key.
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<ApiKeyAuditFilter>();
        });

        // Singleton: it holds one row for the life of the process and is read on the sign-in path,
        // the token-issuing path and every password validator. It resolves its scoped DbContext
        // through a scope factory rather than capturing one — see SecurityPolicyCache.
        services.AddSingleton<SecurityPolicyCache>();

        // The forced-two-factor steering. The requirement is attached to the panel's authorization
        // policies by the Host (RolePolicies); the handler that decides it lives here, with the
        // module that issues the claim it reads.
        services.AddSingleton<IAuthorizationHandler, TwoFactorEnrolmentCompleteHandler>();

        // View as (issue #317): the endpoint boundary a view-as token may not cross, attached to the
        // policies by the Host like the steering above; and the per-request check of the view-as
        // session, scoped because it reads the request's own DbContext (the bearer handler resolves it).
        services.AddSingleton<IAuthorizationHandler, ViewAsBoundaryHandler>();
        services.AddScoped<ViewAsSessionVerifier>();

        // The two halves of a view-as start that both start endpoints share (issues #317, #179): the
        // operator's standing and the signing of the token, and the naming of what is being viewed.
        // Scoped, because both read the request's own DbContext and the tenant-scoped windows onto
        // the accounts module.
        services.AddScoped<ViewAsStarter>();
        services.AddScoped<ViewAsSubjectReader>();

        // Suspension per membership (issues #326, #331): the request-time check that a customer token
        // is not speaking for an account its login was suspended from, attached to the customer-level
        // policy by the Host. Scoped, because it reads the request's own DbContext.
        services.AddScoped<IAuthorizationHandler, AccountMembershipActiveHandler>();

        // Scoped, because it counts through the request's own DbContext and saves alongside the
        // audit entry for the same refused attempt.
        services.AddScoped<BruteForceDetector>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<ISessionService, SessionService>();
        // Scoped, because it reads the request's own DbContext. Shared by sign-in and refresh so both
        // choose the account a session speaks for the same way.
        services.AddScoped<ActiveAccountResolver>();
        services.AddScoped<AuthenticationCompleter>();
        services.AddScoped<ITotpService, TotpService>();
        services.AddScoped<IRecoveryCodeService, RecoveryCodeService>();
        services.AddScoped<TwoFactorCodeRejection>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against; see AccountsModule for the full reasoning. Module-internal
        // lookups inject IStringLocalizer<T> directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(IdentityModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(IdentityModule).Assembly));
    }
}
