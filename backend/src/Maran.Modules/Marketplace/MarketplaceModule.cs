using System.Resources;
using Maran.Modules.Marketplace.Extensions;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Jobs;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Persistence;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Validators;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace;

/// <summary>
/// The Marketplace module's entry point (rules/csharp.md "Canonical backend layout"). Lets an
/// administrator, once they have switched it on, browse the Innovayse module catalogue and have a verified
/// module package staged for the loader, and keeps the record of what it staged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inert until an administrator switches it on.</b> Nothing here runs at start-up: there is no hosted
/// service, no timer and no start-up call, and the options are validated on first use and not at boot, so
/// a server that never uses the marketplace starts whatever its configuration holds. The switch is a
/// database row that is off by default (<see cref="Services.MarketplaceSettingsStore"/>).
/// </para>
/// <para>
/// Registers <see cref="MarketplaceDbContext"/> against the <c>marketplace</c> schema, the ledger of staged
/// packages, the switch store, the per-module operation gate and the audit journal. The transport, the
/// catalogue client and the downloader are registered by <c>AddMarketplaceClient</c>, which sits beside
/// them in <c>Extensions/</c>.
/// </para>
/// </remarks>
public sealed class MarketplaceModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Marketplace.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Marketplace.Resources.DisplayNames";

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
            return MarketplaceManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Bound, and validated by the first read of the options, never at start-up: a server that never
        // turns the marketplace on must boot whatever its panel.env says (rules/security.md item 10).
        // There is deliberately no ValidateOnStart here.
        services.AddSingleton<IValidateOptions<MarketplaceOptions>, MarketplaceOptionsValidator>();
        services.AddOptions<MarketplaceOptions>()
            .Bind(configuration.GetSection(MarketplaceOptions.SectionName));

        // Scoped by default. Nothing this module stores is tenant-owned, but a singleton DbContext is
        // shared mutable state across concurrent requests whatever it holds.
        services.AddDbContext<MarketplaceDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, MarketplaceModuleMigrations>();

        // Scoped, because each reads or writes the request's own DbContext.
        services.AddScoped<InstalledPackageLedger>();
        services.AddScoped<MarketplaceSettingsStore>();
        services.AddScoped<ModuleOperationGate>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<MarketplaceAuditJournal>();

        // A singleton, because it is process memory that outlives a request: what the last update check
        // found withdrawn (so the installed list can say so without asking the marketplace again). The task
        // of an operation still running is NOT memory: it is on the operation gate's claim row.
        services.AddSingleton<ObservedRevocations>();

        // Scoped, because each reads the request's own DbContext, the caller's language or both. The one
        // place a module version's eligibility is decided, and the services the commands and queries share.
        services.AddScoped<InstalledModuleReader>();
        services.AddScoped<InstallEligibility>();
        services.AddScoped<MarketplaceDisplayNames>();
        services.AddScoped<MarketplaceCatalogueProjector>();
        services.AddScoped<MarketplaceStatusReader>();
        services.AddScoped<ModuleOfferResolver>();
        services.AddScoped<ModuleOperationStarter>();
        services.AddScoped<ModuleOperationRunner>();
        services.AddScoped<IModuleOperationDispatcher, MessageBusModuleOperationDispatcher>();

        // Scoped, resolved once per Wolverine message, the way the Backups module registers its own message
        // handlers: it is what runs when an accepted install, update, remove or rollback arrives.
        services.AddScoped<ModuleOperationHandler>();

        // NOTE: the transport, the catalogue client and the downloader are registered by the client
        // extension and not here, so that the one place a request can leave the process (the HTTP
        // transport, gated by the switch row and by the validated options) is read in one file, and
        // this method stays a list of the module's own state. The extension adds no hosted service and
        // makes no call while it registers.
        services.AddMarketplaceClient();

        // The shared resource pools the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> directly.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(MarketplaceModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(MarketplaceModule).Assembly));
    }
}
