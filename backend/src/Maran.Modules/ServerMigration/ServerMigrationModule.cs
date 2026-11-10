using System.Resources;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Jobs;
using Maran.Modules.ServerMigration.Persistence;
using Maran.Modules.ServerMigration.Queries.GetAccountImportReport;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration;

/// <summary>
/// The ServerMigration module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="ServerMigrationDbContext"/> against the <c>servermigration</c> PostgreSQL schema and contributes the module's
/// controllers to the Host's routing. Owns the registry of remote servers an administrator reads from: the key the
/// panel generated for each, the host key pinned for it, and the inventory discovered on it (issue #197).
/// </summary>
public sealed class ServerMigrationModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.ServerMigration.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/ValidationMessages*.resx</c>.</summary>
    private const string ValidationMessagesResourceBaseName = "Maran.Modules.ServerMigration.Resources.ValidationMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.ServerMigration.Resources.DisplayNames";

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
            return ServerMigrationManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default: a singleton DbContext is shared mutable state across concurrent requests whatever it
        // holds. The context takes the panel's cipher for the private key column.
        services.AddDbContext<ServerMigrationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, ServerMigrationModuleMigrations>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<ServerMigrationAuditJournal>();

        // Moving one account between Maran servers (issue #127): the signing key, the strict package reader and
        // the writer, the import job, and the report the start command judges again.
        services.AddScoped<MigrationSigningKeys>();
        services.AddScoped<MigrationPackageWriter>();
        services.AddScoped<AccountImportStager>();
        services.AddScoped<AccountImportFailureNames>();
        services.AddScoped<AccountImportOrchestrator>();
        services.AddScoped<AccountImportRequestedHandler>();
        services.AddScoped<AccountExportExpiryHandler>();
        services.AddScoped<GetAccountImportReportQueryHandler>();

        // The pull transport of an account move (issue #127). The source side: the grant the Host's handler asks
        // about, and the ledger that spends it under a row lock. The receiving side: the named client whose
        // connector refuses a forbidden address, and the client over it. The Host adds the pipeline.
        // Moving a whole server (issue #197, phase 2): the dry run, the two-phase engine with its scheduler and the
        // registry that lets one request stop a transfer another is running, the verification that asks this
        // panel's own web server, and the DNS helper. The registry is a singleton because a worker is a task of
        // this process.
        services.AddSingleton<MigrationRunRegistry>();
        services.AddSingleton<IDomainResolver, SystemDomainResolver>();
        services.AddScoped<ISiteProbe, LoopbackSiteProbe>();
        services.AddScoped<MigrationVerifier>();
        services.AddScoped<MigrationJobPlanner>();
        services.AddScoped<MigrationAccountCanceller>();
        services.AddScoped<MigrationAccountTransfer>();
        services.AddScoped<MigrationJobScheduler>();
        services.AddScoped<MigrationAccountTransferRequestedHandler>();
        services.AddScoped<MigrationJobTickRequestedHandler>();

        services.AddScoped<IMigrationGrantAuthenticator, MigrationGrantAuthenticator>();
        services.AddScoped<IExportGrantLedger, ExportGrantLedger>();
        services.AddSingleton<ILocalAddressSource, LocalAddressSource>();
        services.AddSingleton<PullConnector>();
        services.AddScoped<IMigrationPullClient, MigrationPullClient>();
        services.AddHttpClient(MigrationPullClient.HttpClientName, MigrationPullClient.ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                return provider.GetRequiredService<PullConnector>().CreateHandler();
            });

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> directly.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(ServerMigrationModule).Assembly));
        services.AddSingleton(new ResourceManager(ValidationMessagesResourceBaseName, typeof(ServerMigrationModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(ServerMigrationModule).Assembly));
    }
}
