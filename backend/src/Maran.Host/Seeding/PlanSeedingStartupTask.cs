using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;

namespace Maran.Host.Seeding;

/// <summary>
/// Runs <see cref="PlanSeeder"/> once, at startup, so a freshly installed panel has the standard
/// plans an account can be created against.
/// </summary>
/// <remarks>
/// This exists because the seeder had no caller at all. It was written, unit-tested and given fixed
/// ids for idempotency, and then nothing ever invoked it: not the Host, not a module, not the
/// installer. The consequence was not subtle — a fresh server had an empty <c>accounts.plans</c>
/// table, <c>Account.PlanId</c> carries a real foreign key to it, and so the panel's very first
/// operation, creating an account, was impossible on every new installation. Every test passed,
/// because every test seeds a plan of its own before it needs one.
///
/// A hosted service rather than a line in <c>Program.Main</c>: seeding touches the database, which
/// means it must happen after the container is built and inside a scope, and a hosted service is the
/// composition root's own vocabulary for "do this once when the panel starts"
/// (rules/architecture.md — <c>Maran.Host</c> composes, it holds no business logic; the seed data
/// itself belongs to the Accounts module and stays there).
///
/// It is deliberately NOT the thing that applies migrations. The installer and the update command
/// apply EF migrations, having taken a database dump first; a process start that reshapes tables as
/// a side effect is exactly what <c>MessagingExtensions</c> refuses for the panel's own schema. So
/// this task assumes the schema is present, which on a real server it is by the time the API is
/// started.
///
/// Idempotence is the seeder's, not this task's: it inserts by fixed id only what is absent and
/// UPDATES nothing. An operator who has edited the Business plan's disk quota keeps their edit
/// across every restart, and an operator who has deleted a standard plan outright gets it back —
/// which is the honest reading of "a fresh installation ships with these", and is why the seeder
/// does not track deletions.
/// </remarks>
public sealed class PlanSeedingStartupTask : IHostedService
{
    /// <summary>Pre-compiled log delegate for a start with no database configured at all.</summary>
    private static readonly Action<ILogger, Exception?> LogNoDatabase =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(PlanSeedingStartupTask)),
            "No database is configured, so the standard plans were not seeded");

    /// <summary>Pre-compiled log delegate for a seed the database refused.</summary>
    private static readonly Action<ILogger, Exception?> LogSeedFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(PlanSeedingStartupTask)),
            "The standard plans could not be seeded; no account can be created until a plan exists");

    /// <summary>Opens the scope the module's database context is resolved from.</summary>
    /// <remarks>
    /// A scope factory, not the context: <see cref="AccountsDbContext"/> is scoped and this service
    /// is a singleton, and a singleton capturing a scoped dependency is refused by the container at
    /// build time — which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Whether this panel has a database to seed into.</summary>
    private readonly bool _hasDatabase;

    /// <summary>Where a seed that did not happen is reported.</summary>
    private readonly ILogger<PlanSeedingStartupTask> _logger;

    /// <summary>Creates the startup task.</summary>
    /// <param name="scopeFactory">Opens the scope the database context is resolved from.</param>
    /// <param name="configuration">Read once, to tell a configured panel from a shell run.</param>
    /// <param name="logger">Where a seed that did not happen is reported.</param>
    public PlanSeedingStartupTask(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PlanSeedingStartupTask> logger)
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

    /// <summary>Seeds the standard plans, reporting rather than throwing when it cannot.</summary>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    /// <remarks>
    /// A failure is logged at Error and swallowed. Throwing here would stop the panel from starting
    /// at all, which turns a transient database problem on a restart into an outage of sessions,
    /// sites, logs and health — every one of which would otherwise have kept working. The log line
    /// says what is broken and what it costs, and the panel's own refusal to create an account
    /// without a plan is what stops the failure from being silent to an operator.
    /// </remarks>
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
            var dbContext = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

            await new PlanSeeder(dbContext).SeedAsync(cancellationToken);
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
