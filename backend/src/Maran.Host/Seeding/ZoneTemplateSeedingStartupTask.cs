using Maran.Modules.Dns.Seeders;

namespace Maran.Host.Seeding;

/// <summary>
/// Runs <see cref="ZoneTemplateSeeder"/> once, at startup, so a freshly installed panel has the built-in zone
/// templates and a default one for the zones it creates for new sites.
/// </summary>
/// <remarks>
/// <para>
/// The split is the same one <see cref="PlanSeedingStartupTask"/> makes and for the same reason: the Dns module
/// owns WHAT is seeded, and the composition root owns the fact that it runs at startup. A module may reference
/// only the Sdk and the SharedKernel, so it cannot register a hosted service of its own.
/// </para>
/// <para>
/// A failure is logged and swallowed. Throwing would stop the panel from starting over a template row, and the
/// seed is idempotent, so a failed one is retried by the next start at no cost. A panel that ran without it
/// still creates empty zones.
/// </para>
/// </remarks>
public sealed class ZoneTemplateSeedingStartupTask : IHostedService
{
    /// <summary>Pre-compiled log delegate for a start with no database configured at all.</summary>
    private static readonly Action<ILogger, Exception?> LogNoDatabase =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(ZoneTemplateSeedingStartupTask)),
            "No database is configured, so the DNS zone templates were not seeded");

    /// <summary>Pre-compiled log delegate for a seed the database refused.</summary>
    private static readonly Action<ILogger, Exception?> LogSeedFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(ZoneTemplateSeedingStartupTask)),
            "The DNS zone templates could not be seeded; new zones are created empty until the next start");

    /// <summary>Opens the scope the module's database context is resolved from.</summary>
    /// <remarks>
    /// A scope factory, not the context: <c>DnsDbContext</c> is scoped and this service is a singleton, and a
    /// singleton capturing a scoped dependency is refused by the container at build time.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether this panel has a database to seed into.</summary>
    private readonly bool _hasDatabase;

    /// <summary>Where a seed that did not happen is reported.</summary>
    private readonly ILogger<ZoneTemplateSeedingStartupTask> _logger;

    /// <summary>Creates the startup task.</summary>
    /// <param name="scopeFactory">Opens the scope the database context is resolved from.</param>
    /// <param name="configuration">Read once, to tell a configured panel from a shell run.</param>
    /// <param name="logger">Where a seed that did not happen is reported.</param>
    public ZoneTemplateSeedingStartupTask(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ZoneTemplateSeedingStartupTask> logger)
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

    /// <summary>Seeds the templates, reporting rather than throwing when it cannot.</summary>
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
            var seeder = scope.ServiceProvider.GetRequiredService<ZoneTemplateSeeder>();

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

    /// <summary>Nothing to stop: the seed runs once at startup and holds no resources.</summary>
    /// <param name="cancellationToken">Cancelled when shutdown must not wait any longer.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
