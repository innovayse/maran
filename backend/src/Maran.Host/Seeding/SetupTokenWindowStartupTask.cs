using Maran.Modules.Identity.Seeders;

namespace Maran.Host.Seeding;

/// <summary>
/// Runs <see cref="SetupTokenWindowSeeder"/> at every start, so the installer's one-time setup token
/// has a recorded age and an install nobody finishes stops being claimable.
/// </summary>
/// <remarks>
/// <para>
/// The split is the one <see cref="PlanSeedingStartupTask"/> makes: the Identity module owns WHAT is
/// recorded, and the composition root owns the fact that it happens at startup. A module may
/// reference only the Sdk and the SharedKernel, so it cannot register a hosted service of its own.
/// </para>
/// <para>
/// This one is not reference data, and that is worth saying because it is filed beside three seeders
/// that are. It records a MEASUREMENT — the first instant this panel ran with a given token
/// configured — and the only moment that can be measured is the start itself. Nothing else on the
/// server knows it: the installer writes the token with no issue time beside it.
/// </para>
/// <para>
/// A failure is logged and swallowed, like the seeders beside it. Throwing would stop a panel from
/// starting over a single row, and what the failure costs is bounded and self-repairing: with no row
/// the setup handler opens the window on the first attempt instead, so the token's life is shortened
/// from its first use rather than from this start, and the next restart records it properly.
/// </para>
/// </remarks>
public sealed class SetupTokenWindowStartupTask : IHostedService
{
    /// <summary>Pre-compiled log delegate for a start with no database configured at all.</summary>
    private static readonly Action<ILogger, Exception?> LogNoDatabase =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(SetupTokenWindowStartupTask)),
            "No database is configured, so the setup token's expiry window was not opened");

    /// <summary>Pre-compiled log delegate for a window the database refused to record.</summary>
    private static readonly Action<ILogger, Exception?> LogFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(SetupTokenWindowStartupTask)),
            "The setup token's expiry window could not be recorded; the token's life will be counted "
            + "from the first attempt to use it instead of from this start");

    /// <summary>Opens the scope the module's database context is resolved from.</summary>
    /// <remarks>
    /// A scope factory, not the seeder: the seeder holds a scoped <c>IdentityDbContext</c> and this
    /// service is a singleton, and a singleton capturing a scoped dependency is refused by the
    /// container at build time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether this panel has a database to record into.</summary>
    private readonly bool _hasDatabase;

    /// <summary>Where a window that was not recorded is reported.</summary>
    private readonly ILogger<SetupTokenWindowStartupTask> _logger;

    /// <summary>Creates the startup task.</summary>
    /// <param name="scopeFactory">Opens the scope the database context is resolved from.</param>
    /// <param name="configuration">Read once, to tell a configured panel from a shell run.</param>
    /// <param name="logger">Where a window that was not recorded is reported.</param>
    public SetupTokenWindowStartupTask(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SetupTokenWindowStartupTask> logger)
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

    /// <summary>Records the token's window, reporting rather than throwing when it cannot.</summary>
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
            var seeder = scope.ServiceProvider.GetRequiredService<SetupTokenWindowSeeder>();

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

    /// <summary>Nothing to stop: the record is written once at startup and holds no resources.</summary>
    /// <param name="cancellationToken">Cancelled when shutdown must not wait any longer.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
