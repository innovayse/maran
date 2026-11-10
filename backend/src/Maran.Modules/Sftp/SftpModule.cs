using System.Resources;
using Maran.Modules.Sftp.Interfaces;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp;

/// <summary>
/// The Sftp module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="SftpDbContext"/> against the <c>sftp</c> PostgreSQL schema and contributes the
/// module's controllers to the Host's routing. Owns customer file-transfer logins: which account
/// asked for one and what the host calls it (spec §11).
/// </summary>
public sealed class SftpModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Sftp.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Sftp.Resources.DisplayNames";

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
            return SftpManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default, which is what the tenant filter requires: the context closes over the
        // request's own ICurrentUser, so a singleton context would freeze one caller's tenant scope
        // and serve it to everybody.
        services.AddDbContext<SftpDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // A singleton, because it holds no state: it opens its own scope per call, so the Host can run the
        // module's migrations through the root provider (rules/architecture.md "The database evolves the same way").
        services.AddSingleton<IModuleMigrations, SftpModuleMigrations>();

        // Scoped, because it reads the request's own tenant-scoped DbContext: this module's answer to the
        // personal-data export, an allow-list of fields and never a secret.
        services.AddScoped<IPersonalDataProvider, SftpPersonalDataProvider>();

        // Scoped, because it reads the request's own ICurrentUser for the journal's actor.
        services.AddScoped<SftpAuditJournal>();

        // Scoped, because it takes a transaction on the request's own DbContext connection: the
        // advisory lock it holds is scoped to that transaction and must be released by its commit.
        services.AddScoped<ISftpUserSlotGate, SftpUserSlotGate>();

        // Scoped, because it opens a transaction on the request's own DbContext connection and the row
        // lock it takes is released by that transaction's commit or rollback.
        services.AddScoped<ISftpUserKeyLock, SftpUserKeyLock>();

        // This module's answer to how many SFTP logins an account owns, for the Accounts module's resource
        // summary. Scoped, because it reads through the request-scoped, tenant-filtered context.
        services.AddScoped<IAccountResourceCounter, SftpAccountResourceCounter>();

        // Moving an account to another Maran server (issue #127): what this module hands over, and how it
        // creates what it is handed. The Migration module gathers every implementation through the Sdk.
        services.AddScoped<IAccountMigrationExporter, SftpMigrationExporter>();
        services.AddScoped<IAccountMigrationImporter, SftpMigrationImporter>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T>
        // directly instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(SftpModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(SftpModule).Assembly));
    }
}
