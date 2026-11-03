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

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<AccountAuditJournal>();

        // Scoped, because it reads the request's DbContext and resolves shipped plans' names in the
        // request's culture.
        services.AddScoped<PlanNameDirectory>();

        // Scoped, because it reads the request's own DbContext: what a reseller owns, measured for the
        // reseller screens and the answers that carry an envelope beside its usage.
        services.AddScoped<ResellerUsageReader>();

        // Scoped, because it reads the request's own ICurrentUser and DbContext: which reseller, if any,
        // the caller is, and whether that reseller may still write.
        services.AddScoped<ResellerScope>();

        // Scoped, because it loads the reseller row into the handler's own DbContext: the one place an
        // allocation is checked against the envelope and claimed before an account or a plan is saved.
        services.AddScoped<ResellerAllocationGuard>();

        // Scoped, because it reads the request's own DbContext: restates an owner id as the reseller's
        // name for the owner columns the screens show.
        services.AddScoped<ResellerNameDirectory>();

        // Scoped, because it reads the request's own DbContext, deliberately past the tenant filters: an
        // account name or a domain is claimed once across every owner.
        services.AddScoped<AccountNamespaceClaims>();

        // Holds no state of its own; scoped like the handlers that take it. Shared by a plan's quota
        // edit and an account's plan change, so both measure and refuse the same way.
        services.AddScoped<AccountDiskQuotaEnforcer>();
        services.AddScoped<AccountRuntimeLimitsEnforcer>();

        // The one window other modules have onto this module's data (rules/architecture.md
        // "Cross-module needs go through Wolverine messages or Sdk abstractions"). Scoped, because
        // it reads the request's own DbContext and applies the request's own tenant scope.
        services.AddScoped<IAccountDirectory, AccountDirectory>();

        // The reseller counterpart of the window above, for the facts Identity needs about a reseller it
        // may not read the tables of: its name and whether it is suspended. Scoped, because it applies
        // the request's own tenant scope.
        services.AddScoped<IResellerDirectory, ResellerDirectory>();

        // The unscoped, names-only window behind the header's account selector: a login that holds
        // several accounts must read the names of all of them, not only the one it is acting for.
        services.AddScoped<IAccountNameDirectory, AccountNameDirectory>();

        // The window the plan-feature filter asks through (Sdk RequiresAccountFeatureFilter): whether
        // an account's plan includes a feature. Scoped for the same reason as the directory above.
        services.AddScoped<IAccountFeatureDirectory, AccountFeatureDirectory>();

        // Scoped like the Databases module's own refusal resolver: it resolves the sentences an
        // administrator reads against IStringLocalizer<DisplayNames>, which is culture-sensitive per
        // request.
        services.AddScoped<HomeGroupRepairRefusalDisplayNames>();

        // Pairs every composed module's resource count with the plan's limits, for both the
        // administrator's and the customer's summary. Scoped, because it reads the request's own
        // tenant-scoped context and the counters, which are scoped too.
        services.AddScoped<AccountResourceSummaryReader>();

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
}
