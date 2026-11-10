using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Dns.Services;

/// <summary>
/// Removes the DNS-01 challenge records a certificate order left behind, because the process that published
/// them died before it removed them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The certificate facility removes its record in a <c>finally</c>, which covers a
/// failed validation and a cancelled one but not a crash between publishing and removing. Such a record is
/// harmless to validation but would sit in the customer's zone for ever, and two values at one name are all a
/// wildcard order may use. A challenge record older than <see cref="ChallengeRecordPolicy.MaxAge"/> belongs to
/// no validation still waiting (the longest wait is minutes), so it is taken out.
/// </para>
/// <para>
/// <b>It touches challenge records only.</b> The removal is <see cref="Zone.RemoveStaleChallengeRecords"/>,
/// which never reads a record of any other origin, so a customer's own TXT at the same name is as safe here as
/// it is anywhere else. Each zone is its own edit, under the zone's lock, published to the agent first like
/// every other change.
/// </para>
/// <para>
/// <b>It reads every tenant's zones, on purpose.</b> There is no signed-in user, and a tenant-filtered read
/// would see nothing. It does nothing while the name server is not in service, and it is not reachable from
/// any request. One zone's failure never stops the rest.
/// </para>
/// </remarks>
public sealed class DnsChallengeSweeper : BackgroundService
{
    /// <summary>How long between passes.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, Exception?> LogSwept =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(1, nameof(DnsChallengeSweeper)),
            "Removed stale certificate validation records from {Zones} DNS zones; {Failed} zones could not be swept");

    /// <summary>Pre-compiled log delegate for a zone that could not be swept.</summary>
    private static readonly Action<ILogger, string, Exception?> LogZoneFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(DnsChallengeSweeper)),
            "Could not sweep the certificate validation records of a DNS zone: {Code}");

    /// <summary>Pre-compiled log delegate for a pass that threw.</summary>
    private static readonly Action<ILogger, Exception?> LogPassFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(DnsChallengeSweeper)),
            "A sweep of stale certificate validation records failed; the next pass tries again");

    /// <summary>Opens one scope per pass and per zone to resolve the module's scoped services from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<DnsChallengeSweeper> _logger;

    /// <summary>Creates the sweeper.</summary>
    /// <param name="scopeFactory">Opens the scopes each pass resolves its dependencies from.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public DnsChallengeSweeper(IServiceScopeFactory scopeFactory, ILogger<DnsChallengeSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Removes every challenge record older than the policy's age, zone by zone.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many zones were swept; a zone that could not be swept is logged and not counted.</returns>
    /// <remarks>
    /// Public because it is the pass and the pass is what has behaviour worth asserting; a test drives it
    /// directly instead of starting a hosted service and waiting on a timer.
    /// </remarks>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StaleZone> stale;
        DateTimeOffset cutoff;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
            cutoff = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow - ChallengeRecordPolicy.MaxAge;

            var settings = await dbContext.DnsSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
            if (settings is null || !settings.Enabled || !settings.IsComplete)
            {
                return 0;
            }

#pragma warning disable RS0030 // no principal at all: every account's zones are read to find the records no order is waiting on
            stale = await dbContext.ZoneRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(record => record.Origin == RecordOrigin.AcmeChallenge && record.CreatedAt < cutoff)
                .Select(record => new StaleZone(record.Zone.AccountId, record.ZoneId))
                .Distinct()
                .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        }

        var swept = 0;
        var failed = 0;
        foreach (var zone in stale)
        {
            if (await SweepZoneAsync(zone, cutoff, cancellationToken))
            {
                swept++;
            }
            else
            {
                failed++;
            }
        }

        LogSwept(_logger, swept, failed, null);
        return swept;
    }

    /// <summary>Runs a pass at start and then on every interval, until the host stops.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // A database that is not reachable yet must not end the service for the life of the process.
                    LogPassFailed(_logger, exception);
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and not logged as one.
        }
    }

    /// <summary>Sweeps one zone as an edit of its own, in a scope of its own.</summary>
    /// <param name="zone">The zone and its owner.</param>
    /// <param name="cutoff">Records created before this instant are removed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>True when the zone was swept or had nothing left to sweep.</returns>
    private async Task<bool> SweepZoneAsync(StaleZone zone, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var editor = scope.ServiceProvider.GetRequiredService<DnsZoneEditor>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var opened = await editor.OpenForAccountAsync(zone.AccountId, zone.Id, cancellationToken);
            if (!opened.IsSuccess)
            {
                // A zone deleted since the listing has nothing left to sweep.
                if (opened.Error!.Type == ErrorType.NotFound)
                {
                    return true;
                }

                LogZoneFailed(_logger, opened.Error.Code, null);
                return false;
            }

            await using var edit = opened.Value;
            if (edit.Zone.RemoveStaleChallengeRecords(cutoff, clock.UtcNow) == 0)
            {
                return true;
            }

            var published = await editor.PublishAsync(edit, cancellationToken);
            if (!published.IsSuccess)
            {
                LogZoneFailed(_logger, published.Error!.Code, null);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogZoneFailed(_logger, exception.GetType().Name, exception);
            return false;
        }
    }

    /// <summary>A zone holding a stale challenge record, and the account that owns it.</summary>
    /// <param name="AccountId">The owning account.</param>
    /// <param name="Id">The zone's identity.</param>
    private sealed record StaleZone(Guid AccountId, Guid Id);
}
