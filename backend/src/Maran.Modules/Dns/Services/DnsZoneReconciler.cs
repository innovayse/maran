using Maran.Agent.Client.Interfaces;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Interfaces;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// Brings what the name server serves back to what the database says, once, when the panel starts: a zone
/// whose served serial differs from its row is published again, and a served zone with no row is removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The database is the truth and the zone file is derived from it
/// (rules/architecture.md). Every edit tells the agent first and saves second (<see cref="DnsZoneEditor"/>),
/// so a save that fails after the agent applied leaves the daemon serving newer data than the row; a
/// restored database, a lost zone directory or a zone file removed by hand leaves the opposite. Nothing else
/// notices. This pass compares the two by serial, which is what changes with every edit, and makes the host
/// match the row.
/// </para>
/// <para>
/// <b>The row always wins, and the serial still only increases.</b> A zone the daemon serves at a serial
/// BEHIND its row, or does not serve at all, is published from the row as it is. A zone the daemon serves at
/// a serial AHEAD of its row (the failed save above) is published from the row under a serial moved past the
/// served one (<see cref="Zone.Reissue"/>) and the row is saved with it, so a secondary that already saw the
/// higher serial still takes the correction.
/// </para>
/// <para>
/// <b>A served zone with no row is removed, under the name's lock.</b> It would go on answering the
/// internet for a name no account owns. A creation that has put its zone on the host and not yet committed its
/// row (<c>CreateZoneCommandHandler</c> tells the agent first) looks the same, which is why both take the
/// same per-name lock (<see cref="IDnsZoneLock.TakeForNameAsync"/>): the creation holds it from before the
/// host is touched until the row is committed, and this pass looks for the row AFTER taking it. It therefore
/// waits for a creation in flight and then finds its row, and a creation that begins after the check puts
/// its zone on the host itself. The listing the pass started from can still be stale, which only means a
/// zone created since is not in it and is not examined.
/// </para>
/// <para>
/// <b>It does nothing while DNS is not in service</b> (not enabled, or its settings unstated): the daemon is
/// not running, and publishing to it would only fail. Enabling DNS is when an administrator's intention
/// reaches the host, and a restart afterwards publishes whatever is missing.
/// </para>
/// <para>
/// <b>It reads every tenant's zones, on purpose.</b> There is no signed-in user at start-up, and a
/// tenant-filtered read would see nothing at all, which looks exactly like a server with no zones and would
/// then remove every zone the daemon serves. The reads ignore the filter and each is bounded by what it
/// wants. It is not reachable from any request.
/// </para>
/// <para>
/// <b>An agent that is not up yet is expected</b>, as for the Sites module's start-up pass: the panel and the
/// agent are separate units with no ordering guarantee across a reboot. A pass that leaves anything
/// unreconciled is retried up to <see cref="MaximumAttempts"/> times and then abandoned with an error line.
/// One zone's failure never stops the rest. Nothing is journalled: no decision is made here and nothing a
/// user did is being recorded.
/// </para>
/// </remarks>
public sealed class DnsZoneReconciler : BackgroundService
{
    /// <summary>How many times a pass that left something unreconciled is retried before giving up.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>How long between attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>The agent's code for a zone it does not serve, which is an answer and not a failure.</summary>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, int, int, int, Exception?> LogReconciled =
        LoggerMessage.Define<int, int, int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(DnsZoneReconciler)),
            "Reconciled the name server with the database: {InSync} zones in step, {Republished} republished, "
            + "{Removed} unowned zones removed, {Skipped} left to a concurrent edit, {Failed} failed");

    /// <summary>Pre-compiled log delegate for a zone that could not be reconciled.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogZoneFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(DnsZoneReconciler)),
            "Could not reconcile zone {Zone}: {Code}");

    /// <summary>Pre-compiled log delegate for a pass the agent would not complete.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(3, nameof(DnsZoneReconciler)),
            "Could not reconcile every DNS zone on attempt {Attempt}; the name server may serve stale zones");

    /// <summary>Pre-compiled log delegate for giving up.</summary>
    private static readonly Action<ILogger, int, Exception?> LogGaveUp =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(4, nameof(DnsZoneReconciler)),
            "Gave up reconciling DNS zones after {Attempts} attempts; the name server may serve zones that differ "
            + "from the database until the panel is restarted");

    /// <summary>Pre-compiled log delegate for a pass that found DNS not in service.</summary>
    private static readonly Action<ILogger, Exception?> LogNotInService =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(5, nameof(DnsZoneReconciler)),
            "The name server is not in service, so no DNS zone was reconciled");

    /// <summary>Opens one scope per pass to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not a <see cref="DnsDbContext"/>: a <see cref="BackgroundService"/> is a singleton
    /// and the context is scoped, and a singleton capturing a scoped dependency is refused by the container
    /// at build time, which stops the whole API.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<DnsZoneReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="scopeFactory">Opens the scope each pass resolves its dependencies from.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public DnsZoneReconciler(IServiceScopeFactory scopeFactory, ILogger<DnsZoneReconciler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Compares every zone row with what the name server serves and makes the host match.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>
    /// True when nothing failed, including when DNS is not in service and when a zone was left to a
    /// concurrent edit, which no retry can improve. False when the agent refused a call or a zone could not
    /// be published.
    /// </returns>
    /// <remarks>
    /// Sequential, because the agent serialises work and takes its own locks. Public because it is the pass
    /// and the pass is what has behaviour worth asserting; a test drives it directly instead of starting a
    /// hosted service and waiting on a timer.
    /// </remarks>
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentDnsClient>();
        var zoneLock = scope.ServiceProvider.GetRequiredService<IDnsZoneLock>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var settings = await dbContext.DnsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        if (settings is null || !settings.Enabled || !settings.IsComplete)
        {
            LogNotInService(_logger, null);
            return true;
        }

        var listed = await agent.ListZonesAsync(cancellationToken);
        if (!listed.IsSuccess)
        {
            LogZoneFailed(_logger, "(all)", listed.Error!.Code, null);
            return false;
        }

        var served = new HashSet<string>(listed.Value, StringComparer.OrdinalIgnoreCase);

#pragma warning disable RS0030 // startup has no principal; every tenant's zones are compared with the host
        var rows = await dbContext.Zones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(zone => zone.Name)
            .Select(zone => new ZoneRow(zone.Id, zone.Name, zone.Serial))
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        var tally = new Tally();
        foreach (var row in rows)
        {
            await ReconcileZoneAsync(
                dbContext, agent, zoneLock, clock, settings, row, served.Contains(row.Name), tally, cancellationToken);
        }

        var owned = new HashSet<string>(
            rows.Select(row =>
            {
                return row.Name;
            }),
            StringComparer.OrdinalIgnoreCase);
        foreach (var name in served.Where(candidate =>
        {
            return !owned.Contains(candidate);
        }).Order(StringComparer.Ordinal))
        {
            await RemoveUnownedAsync(dbContext, agent, zoneLock, name, tally, cancellationToken);
        }

        LogReconciled(_logger, tally.InSync, tally.Republished, tally.Removed, tally.Skipped, tally.Failed, null);
        return tally.Failed == 0;
    }

    /// <summary>Runs passes until one leaves nothing failed or the attempts run out.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (await AttemptAsync(attempt, stoppingToken))
                {
                    return;
                }

                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }

            LogGaveUp(_logger, MaximumAttempts, null);
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and not logged as one.
        }
    }

    /// <summary>Compares one zone's row with what is served and publishes it again when they differ.</summary>
    /// <param name="dbContext">The pass's context.</param>
    /// <param name="agent">The agent's DNS operations.</param>
    /// <param name="zoneLock">Serialises this with every edit of the zone.</param>
    /// <param name="clock">The clock a reissued serial is taken from.</param>
    /// <param name="settings">The settings every zone is published with.</param>
    /// <param name="row">The zone as listed at the start of the pass.</param>
    /// <param name="isServed">Whether the daemon lists the zone.</param>
    /// <param name="tally">Where the outcome is counted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>
    /// The zone is taken under the same lock an edit takes and loaded again AFTER it, so a pass can neither
    /// publish a record set an edit has just replaced nor overwrite the edit's serial. If the serial moved
    /// since the listing, an edit published the zone meanwhile and this pass leaves it alone.
    /// </remarks>
    private async Task ReconcileZoneAsync(
        DnsDbContext dbContext,
        IAgentDnsClient agent,
        IDnsZoneLock zoneLock,
        IClock clock,
        DnsSettings settings,
        ZoneRow row,
        bool isServed,
        Tally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            long? servedSerial = null;
            if (isServed)
            {
                var read = await agent.GetZoneAsync(row.Name, cancellationToken);
                if (read.IsSuccess)
                {
                    servedSerial = read.Value.Serial;
                }
                else if (!string.Equals(read.Error!.Code, AgentNotFoundCode, StringComparison.Ordinal))
                {
                    LogZoneFailed(_logger, row.Name, read.Error.Code, null);
                    tally.Failed++;
                    return;
                }
            }

            if (servedSerial == row.Serial)
            {
                tally.InSync++;
                return;
            }

            await using var transaction = await zoneLock.TakeAsync(row.Id, cancellationToken);

#pragma warning disable RS0030 // startup has no principal; the zone and its records are read for the host
            var zone = await dbContext.Zones
                .IgnoreQueryFilters()
                .Include(candidate => candidate.Records)
                .SingleOrDefaultAsync(candidate => candidate.Id == row.Id, cancellationToken);
#pragma warning restore RS0030
            if (zone is null || zone.Serial != row.Serial)
            {
                tally.Skipped++;
                return;
            }

            var reissued = servedSerial is { } ahead && ahead > zone.Serial;
            if (reissued)
            {
                zone.Reissue(servedSerial!.Value, clock.UtcNow);
            }

            var published = await agent.PutZoneAsync(ZoneDefinitionMapper.From(zone, settings), cancellationToken);
            if (!published.IsSuccess)
            {
                LogZoneFailed(_logger, row.Name, published.Error!.Code, null);
                tally.Failed++;
                return;
            }

            if (reissued)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            tally.Republished++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogZoneFailed(_logger, row.Name, exception.GetType().Name, exception);
            tally.Failed++;
        }
        finally
        {
            // One zone's tracked changes must never be saved by the next zone's save.
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>Takes a served zone that no row owns off the name server.</summary>
    /// <param name="dbContext">The pass's context.</param>
    /// <param name="agent">The agent's DNS operations.</param>
    /// <param name="zoneLock">Serialises this with a creation of the same name.</param>
    /// <param name="name">The served zone's apex name.</param>
    /// <param name="tally">Where the outcome is counted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    private async Task RemoveUnownedAsync(
        DnsDbContext dbContext,
        IAgentDnsClient agent,
        IDnsZoneLock zoneLock,
        string name,
        Tally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            // Asked AFTER the name's lock is held, because a creation that has put the zone on the host and not
            // yet committed its row is not an orphan: it holds this same lock until the row is committed.
            await using var transaction = await zoneLock.TakeForNameAsync(name, cancellationToken);

#pragma warning disable RS0030 // startup has no principal; a zone of any account makes this one owned
            var owned = await dbContext.Zones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(zone => zone.Name == name, cancellationToken);
#pragma warning restore RS0030
            if (owned)
            {
                tally.Skipped++;
                return;
            }

            var removed = await agent.DeleteZoneAsync(name, cancellationToken);
            if (removed.IsSuccess || string.Equals(removed.Error!.Code, AgentNotFoundCode, StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                tally.Removed++;
                return;
            }

            LogZoneFailed(_logger, name, removed.Error.Code, null);
            tally.Failed++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogZoneFailed(_logger, name, exception.GetType().Name, exception);
            tally.Failed++;
        }
    }

    /// <summary>Runs one pass, turning anything it throws into a failed attempt.</summary>
    /// <param name="attempt">Which attempt this is, so the log line names it.</param>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>True when the pass left nothing failed.</returns>
    /// <remarks>
    /// A database that is not reachable yet arrives as an exception. Letting it escape
    /// <see cref="ExecuteAsync"/> would stop the service for the life of the process.
    /// </remarks>
    private async Task<bool> AttemptAsync(int attempt, CancellationToken stoppingToken)
    {
        try
        {
            if (await ReconcileAsync(stoppingToken))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAttemptFailed(_logger, attempt, exception);
            return false;
        }

        LogAttemptFailed(_logger, attempt, null);
        return false;
    }

    /// <summary>A zone as the pass listed it: enough to decide whether it needs work.</summary>
    /// <param name="Id">The row's identity.</param>
    /// <param name="Name">The apex name.</param>
    /// <param name="Serial">The row's serial when the pass started.</param>
    private sealed record ZoneRow(Guid Id, string Name, long Serial);

    /// <summary>What a pass did, counted for its one summary line.</summary>
    private sealed class Tally
    {
        /// <summary>Zones whose served serial already matched their row.</summary>
        public int InSync { get; set; }

        /// <summary>Zones published again from their row.</summary>
        public int Republished { get; set; }

        /// <summary>Served zones that no row owned, removed.</summary>
        public int Removed { get; set; }

        /// <summary>Zones left alone because an edit or a creation was in flight.</summary>
        public int Skipped { get; set; }

        /// <summary>Zones the pass could not reconcile.</summary>
        public int Failed { get; set; }
    }
}
