using System.Resources;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Seeders;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups;

/// <summary>
/// The Backups module's entry point (rules/csharp.md "Canonical backend layout"). Registers
/// <see cref="BackupsDbContext"/> against the <c>backups</c> PostgreSQL schema and contributes the
/// module's controllers to the Host's routing. Owns the panel's record of what has been backed up:
/// asking the agent for an archive, listing what exists, and deleting one (spec §11).
/// </summary>
public sealed class BackupsModule : IPanelModule
{
    /// <summary>Configuration key under which the panel's connection string lives.</summary>
    private const string ConnectionStringName = "Panel";

    /// <summary>The embedded resource base name of <c>Resources/ErrorMessages*.resx</c>.</summary>
    private const string ErrorMessagesResourceBaseName = "Maran.Modules.Backups.Resources.ErrorMessages";

    /// <summary>The embedded resource base name of <c>Resources/DisplayNames*.resx</c>.</summary>
    private const string DisplayNamesResourceBaseName = "Maran.Modules.Backups.Resources.DisplayNames";

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
            return BackupsManifest.Instance;
        }
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? string.Empty;

        // Scoped by default, which is what the tenant filter requires: the context closes over the
        // request's own ICurrentUser, so a singleton context would freeze one caller's tenant scope
        // and serve it to everybody.
        services.AddDbContext<BackupsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // This module has no settings of its own, and that is a deliberate absence rather than a
        // gap. It used to bind Backups__LocalRoot, a value the agent never accepts: the agent
        // writes to its own constant root and refuses a local destination that carries a path, so
        // the setting could only ever change what the panel SAID about where archives are. The
        // directory is now read from the agent through the handshake (rules/architecture.md: one
        // statement, read rather than duplicated).

        services.AddScoped<BackupAuditJournal>();

        // The two resolvers that turn a stored value into the words a screen shows. Scoped, because
        // both read the CURRENT REQUEST's culture through IStringLocalizer — a singleton would
        // answer every caller in whichever language happened to be first.
        services.AddScoped<BackupFailureDisplayNames>();
        services.AddScoped<BackupDestinationDisplayNames>();
        services.AddScoped<BackupDestinationResolver>();
        services.AddScoped<DefaultBackupDestinationSeeder>();
        services.AddScoped<BackupRunner>();
        services.AddScoped<RestoreRunner>();

        // The one act this module exposes to another (spec §12). Registered against the Sdk
        // interface, because the Accounts module may not reference this one and reaches it through
        // that contract or not at all — and "not at all" is a state it is built to observe.
        services.AddScoped<IAccountBackupService, AccountBackupService>();

        // Scoped, because both write through the request-scoped context — resolved once per
        // Wolverine message, the same way the Tasks module resolves its own retention handler. The
        // Host schedules BackupRunRequested every five minutes; these are what run when it and the
        // RetentionRequested it publishes arrive.
        services.AddScoped<BackupRunHandler>();
        services.AddScoped<RetentionHandler>();

        // The shared resource pool the panel-wide ResxErrorTextProvider resolves error codes and
        // Manifest.DisplayNameKey against. Module-internal lookups inject IStringLocalizer<T> instead.
        services.AddSingleton(new ResourceManager(ErrorMessagesResourceBaseName, typeof(BackupsModule).Assembly));
        services.AddSingleton(new ResourceManager(DisplayNamesResourceBaseName, typeof(BackupsModule).Assembly));
    }
}
