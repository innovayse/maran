using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Seeders;
using Microsoft.Extensions.Options;

namespace Maran.Host.Seeding;

/// <summary>
/// Runs <see cref="WhitelistSeeder"/> once, at startup, so a freshly installed panel exempts the
/// address its operator installed it from before the brute-force detector can ban them.
/// </summary>
/// <remarks>
/// <para>
/// The split is the same one <see cref="PlanSeedingStartupTask"/> makes and for the same reason: the
/// Firewall module owns WHAT is seeded, and the composition root owns the fact that it runs at
/// startup. A module may reference only the Sdk and the SharedKernel, so it cannot register a hosted
/// service of its own.
/// </para>
/// <para>
/// A failure is logged and swallowed. Throwing would stop the panel from starting over a whitelist
/// row, turning a transient database problem into an outage of sessions, sites and health — and the
/// seed is only ever wanted on a panel that has never had a whitelist, so a failed one is retried by
/// the next start at no cost.
/// </para>
/// </remarks>
public sealed class FirewallWhitelistSeedingStartupTask : IHostedService
{
    /// <summary>Pre-compiled log delegate for a start with no database configured at all.</summary>
    private static readonly Action<ILogger, Exception?> LogNoDatabase =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(FirewallWhitelistSeedingStartupTask)),
            "No database is configured, so the firewall whitelist was not seeded");

    /// <summary>Pre-compiled log delegate for a seed the database refused.</summary>
    private static readonly Action<ILogger, Exception?> LogSeedFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(FirewallWhitelistSeedingStartupTask)),
            "The firewall whitelist could not be seeded; nothing exempts this server's administrator "
            + "from an automatic ban until a row is added in the panel");

    /// <summary>Opens the scope the module's database context is resolved from.</summary>
    /// <remarks>
    /// A scope factory, not the context: <c>FirewallDbContext</c> is scoped and this service is a
    /// singleton, and a singleton capturing a scoped dependency is refused by the container at build
    /// time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether this panel has a database to seed into.</summary>
    private readonly bool _hasDatabase;

    /// <summary>Where a seed that did not happen is reported.</summary>
    private readonly ILogger<FirewallWhitelistSeedingStartupTask> _logger;

    /// <summary>Creates the startup task.</summary>
    /// <param name="scopeFactory">Opens the scope the database context is resolved from.</param>
    /// <param name="configuration">Read once, to tell a configured panel from a shell run.</param>
    /// <param name="logger">Where a seed that did not happen is reported.</param>
    public FirewallWhitelistSeedingStartupTask(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<FirewallWhitelistSeedingStartupTask> logger)
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

    /// <summary>Seeds the whitelist, reporting rather than throwing when it cannot.</summary>
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
            var seeder = scope.ServiceProvider.GetRequiredService<WhitelistSeeder>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<FirewallOptions>>();

            await seeder.SeedAsync(options.Value, cancellationToken);
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

    /// <summary>Nothing to stop: the seed runs once at startup and holds no resources.</summary>
    /// <param name="cancellationToken">Cancelled when shutdown must not wait any longer.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
