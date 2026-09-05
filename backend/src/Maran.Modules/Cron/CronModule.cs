using System.Resources;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Cron;

/// <summary>
/// The Cron module's entry point (rules/csharp.md "Canonical backend layout"). Contributes the
/// module's controllers to the Host's routing and registers its audit journal. Owns the panel's
/// side of per-account scheduled tasks (spec §11).
/// </summary>
/// <remarks>
/// <b>It registers no <c>DbContext</c> and owns no PostgreSQL schema, and that is the module's
/// central design decision rather than a gap.</b> A cron entry lives in the account's own crontab,
/// which the account can edit directly over SFTP, so a panel table would be a second answer to
/// "what does this account run" that goes stale the first time the customer edits their crontab —
/// and the panel's copy is the one an operator would trust. Everything this module reads, it reads
/// through the agent, and everything it changes, it changes there.
///
/// Two consequences worth stating where a reader meets the module: the plan's entry allowance is
/// counted against what the agent reports rather than against rows here (<c>Commands/CreateCronEntry</c>),
/// and the tenant boundary cannot be a query filter — it is the resolution of an account id to a
/// system user name through <see cref="IAccountDirectory"/>, which answers null for an account the
/// caller does not own.
/// </remarks>
public sealed class CronModule : IPanelModule
{
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
        // No AddDbContext and no connection string read: this module has no persistence of its own.
        // Scoped, because the journal reads the request's own ICurrentUser for the entry's actor.
        services.AddScoped<CronAuditJournal>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(CronModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(CronModule).Assembly));
    }
}
