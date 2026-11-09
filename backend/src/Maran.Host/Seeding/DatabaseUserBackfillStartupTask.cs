using Maran.Modules.Databases.Seeders;

namespace Maran.Host.Seeding;

/// <summary>
/// Runs <see cref="DatabaseUserBackfillSeeder"/> at every start, so a database created before the users
/// screen existed — or by a release that did not write user rows — has its dedicated user recorded.
/// </summary>
/// <remarks>
/// The module owns WHAT is written and this owns the fact that it runs, the split every seeding task
/// here makes (a module cannot register a hosted service). It is not reference data and is not part of
/// the demo's reference seeds. A failure is logged and the panel starts: the screens simply do not list
/// a database's dedicated user until the next start converges it.
/// </remarks>
public sealed class DatabaseUserBackfillStartupTask : IHostedService
{
    /// <summary>Pre-compiled log delegate for a panel started with no database configured.</summary>
    private static readonly Action<ILogger, Exception?> LogNoDatabase =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(DatabaseUserBackfillStartupTask)),
            "No database is configured, so the database users were not backfilled");

    /// <summary>Pre-compiled log delegate for a backfill that failed.</summary>
    private static readonly Action<ILogger, Exception?> LogFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(DatabaseUserBackfillStartupTask)),
            "The database users could not be backfilled; a database's dedicated user is not listed on the "
            + "users screen until the next start succeeds");

    /// <summary>Creates the per-run scope the seeder is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether a connection string is configured.</summary>
    private readonly bool _hasDatabase;

    /// <summary>Where a failure is reported.</summary>
    private readonly ILogger<DatabaseUserBackfillStartupTask> _logger;

    /// <summary>Creates the task.</summary>
    /// <param name="scopeFactory">Creates the scope the seeder runs in.</param>
    /// <param name="configuration">The panel's configuration, read for the connection string.</param>
    /// <param name="logger">Where a failure is reported.</param>
    public DatabaseUserBackfillStartupTask(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DatabaseUserBackfillStartupTask> logger)
    {
        _scopeFactory = scopeFactory;
        _hasDatabase = !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName));
        _logger = logger;
    }

    /// <summary>The configuration key under which the panel's connection string lives.</summary>
    private static string ConnectionStringName
    {
        get
        {
            return "Panel";
        }
    }

    /// <inheritdoc/>
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
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseUserBackfillSeeder>();

            await seeder.SeedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(_logger, exception);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
