using Maran.Modules.Backups.Seeders;

namespace Maran.Host.Seeding;

/// <summary>
/// Runs <see cref="DefaultBackupDestinationSeeder"/> at startup, so this server has the one
/// destination every backup points at and its recorded path agrees with the configured root.
/// </summary>
/// <remarks>
/// <para>
/// The split is the one <see cref="PlanSeedingStartupTask"/> and
/// <see cref="FirewallWhitelistSeedingStartupTask"/> already make: the module owns WHAT is
/// reconciled, the composition root owns the fact that it happens at startup, because a module may
/// reference only the Sdk and the SharedKernel and cannot register a hosted service.
/// </para>
/// <para>
/// <b>The seeder is resolved optionally, and that is not defensive.</b> The Backups module is a
/// module a panel may be composed without — <c>DeleteAccountCommandHandler</c> is built around
/// exactly that state and audits it under its own action — so asking for the type unconditionally
/// would turn "this panel has no Backups module" into a container exception at boot.
/// </para>
/// <para>
/// A failure is logged and swallowed, as the two sibling tasks do. Throwing would stop the panel
/// from starting over one configuration row, and the reconciliation is retried at no cost by the next
/// start; what is NOT swallowed is the consequence, because a panel with no destination row refuses
/// every backup by name rather than quietly rebuilding one from configuration.
/// </para>
/// </remarks>
public sealed class BackupDestinationSeedingStartupTask : IHostedService
{
    /// <summary>Pre-compiled log delegate for a start with no database configured at all.</summary>
    private static readonly Action<ILogger, Exception?> LogNoDatabase =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(BackupDestinationSeedingStartupTask)),
            "No database is configured, so the default backup destination was not reconciled");

    /// <summary>Pre-compiled log delegate for a panel composed without the Backups module.</summary>
    private static readonly Action<ILogger, Exception?> LogNoModule =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(BackupDestinationSeedingStartupTask)),
            "This panel has no Backups module, so there is no backup destination to reconcile");

    /// <summary>Pre-compiled log delegate for a reconciliation the database refused.</summary>
    private static readonly Action<ILogger, Exception?> LogSeedFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(3, nameof(BackupDestinationSeedingStartupTask)),
            "The default backup destination could not be reconciled; until it is, every backup this "
            + "panel is asked for is refused with BackupDestinationNotConfigured");

    /// <summary>Opens the scope the module's database context is resolved from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether this panel has a database to reconcile against.</summary>
    private readonly bool _hasDatabase;

    /// <summary>Where a reconciliation that did not happen is reported.</summary>
    private readonly ILogger<BackupDestinationSeedingStartupTask> _logger;

    /// <summary>Creates the startup task.</summary>
    /// <param name="scopeFactory">Opens the scope the database context is resolved from.</param>
    /// <param name="configuration">Read once, to tell a configured panel from a shell run.</param>
    /// <param name="logger">Where a reconciliation that did not happen is reported.</param>
    public BackupDestinationSeedingStartupTask(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<BackupDestinationSeedingStartupTask> logger)
    {
        _scopeFactory = scopeFactory;
        _hasDatabase = !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName));
        _logger = logger;
    }

    /// <summary>Name of the connection string the panel's modules resolve, as in <c>Program</c>.</summary>
    private static string ConnectionStringName
    {
        get
        {
            return "Panel";
        }
    }

    /// <summary>Reconciles the default destination, reporting rather than throwing when it cannot.</summary>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_hasDatabase)
        {
            LogNoDatabase(_logger, null);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var seeder = scope.ServiceProvider.GetService<DefaultBackupDestinationSeeder>();
            if (seeder is null)
            {
                LogNoModule(_logger, null);
                return;
            }

            await seeder.SeedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSeedFailed(_logger, exception);
        }
    }

    /// <summary>Nothing to stop: the reconciliation runs once at startup and holds no resources.</summary>
    /// <param name="cancellationToken">Cancelled when shutdown must not wait any longer.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
