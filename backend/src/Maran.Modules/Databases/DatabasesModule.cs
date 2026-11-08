using System.Resources;
using Maran.Modules.Databases.Interfaces;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Seeders;
using Maran.Modules.Databases.Services;
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

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, DatabasesModuleMigrations>();

        // Scoped, because it reads the request's own tenant-scoped DbContext: this module's answer to the
        // personal-data export, an allow-list of fields and never a secret.
        services.AddScoped<IPersonalDataProvider, DatabasesPersonalDataProvider>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<DatabaseAuditJournal>();

        // Scoped, because it takes a transaction on the request's own DbContext connection: the
        // advisory lock it holds is scoped to that transaction and must be released by its commit.
        services.AddScoped<IDatabaseSlotGate, DatabaseSlotGate>();

        // Scoped, because it counts through the request's own context under a transaction on its connection.
        services.AddScoped<IDatabaseUserLimitGate, DatabaseUserLimitGate>();

        // Scoped, because it uses the request-scoped context; run by the Host's start-up task.
        services.AddScoped<DatabaseUserBackfillSeeder>();

        // The one window another module reads this schema through: names only, tenant-scoped by the
        // context's own filter (rules/architecture.md "A shared facility's contract").
        services.AddScoped<IAccountDatabaseDirectory, AccountDatabaseDirectory>();

        // This module's answer to how many databases an account owns, for the Accounts module's resource
        // summary. Scoped, because it reads through the request-scoped, tenant-filtered context.
        services.AddScoped<IAccountResourceCounter, DatabaseAccountResourceCounter>();

        // Scoped like GrantRepairRefusalDisplayNames, for the same reason: privilege and preset names are
        // resolved per request culture.
        services.AddScoped<DatabasePrivilegeDisplayNames>();

        // Scoped like the other request-culture readers: it resolves the refusal sentences an
        // administrator reads against IStringLocalizer<DisplayNames>, which is culture-sensitive per
        // request.
        services.AddScoped<GrantRepairRefusalDisplayNames>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(DatabasesModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(DatabasesModule).Assembly));
    }
}
