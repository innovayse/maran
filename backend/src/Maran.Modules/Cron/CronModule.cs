using System.Resources;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Cron;

/// <summary>
/// The Cron module's entry point (rules/csharp.md "Canonical backend layout"). Contributes the
/// module's controllers to the Host's routing and registers its audit journal. Owns the panel's
/// side of per-account scheduled tasks (spec §11) and the history of the runs started by hand.
/// </summary>
/// <remarks>
/// <b>The cron entries are not in a table, and that is the module's central design decision rather
/// than a gap.</b> A cron entry lives in the account's own crontab, which the account can edit directly
/// over SFTP, so a panel table would be a second answer to "what does this account run" that goes stale
/// the first time the customer edits their crontab — and the panel's copy is the one an operator would
/// trust. Everything this module reads about an entry, it reads through the agent, and everything it
/// changes, it changes there.
///
/// The one table it does own, <c>cron.CronRuns</c> (<see cref="CronDbContext"/>, issue #158), is the
/// opposite case: the runs an operator or a customer started by hand. The panel is the only party that
/// knows those happened, so the panel is where they are kept, tenant-filtered like every other
/// customer row.
///
/// Two consequences worth stating where a reader meets the module: the plan's entry allowance is
/// counted against what the agent reports rather than against rows here (<c>Commands/CreateCronEntry</c>),
/// and the tenant boundary cannot be a query filter — it is the resolution of an account id to a
/// system user name through <see cref="IAccountDirectory"/>, which answers null for an account the
/// caller does not own.
/// </remarks>
public sealed class CronModule : IPanelModule
{
    /// <summary>The connection string every module context reads.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Cron.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Cron.Resources.DisplayNames";

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
            return CronManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped: the context is per request, and its tenant filter closes over that request's caller.
        services.AddDbContext<CronDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Scoped, because the journal reads the request's own ICurrentUser for the entry's actor.
        services.AddScoped<CronAuditJournal>();

        // This module's answer to how many crontab entries an account owns, for the Accounts module's resource
        // summary. Scoped, because it resolves the account through the request-scoped directory.
        services.AddScoped<IAccountResourceCounter, CronAccountResourceCounter>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(CronModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(CronModule).Assembly));
    }
}
