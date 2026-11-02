using Maran.Modules.Backups.Queries.CountFailedBackups;
using Maran.Modules.Firewall.Queries.CountBans;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetClockSync;
using Maran.Modules.Ssl.Queries.CountExpiringCertificates;
using Maran.Modules.Tasks.Queries.CountFailedTasks;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Reads what on this server is asking to be looked at: the ONE computation behind both the
/// dashboard's attention block and the navigation's needs-attention count.
/// </summary>
/// <remarks>
/// <para>
/// Two callers, one reading. The dashboard draws the five sources; the navigation badge adds them up
/// (<see cref="AttentionTally"/>). Both ask this type, so the badge can never disagree with the block
/// it points at, and a source added here reaches both.
/// </para>
/// <para>
/// <b>Authorization is the caller's, and it comes first.</b> Every source is a query whose own HTTP
/// surface is administrators-only, dispatched on the bus where that policy does not apply. A caller
/// confirms <see cref="ICurrentUser.IsAdmin"/> BEFORE calling this, exactly as the dashboard does.
/// </para>
/// </remarks>
public static class AttentionReader
{
    /// <summary>
    /// How near expiry a certificate has to be to count as needing attention.
    /// </summary>
    /// <remarks>
    /// Thirty days because that is comfortably more than the renewal window of the certificates the
    /// panel issues — ACME renewal is attempted well inside it — so anything still listed here has
    /// had renewal fail repeatedly, or is a certificate the operator uploaded and must replace by
    /// hand. A shorter horizon would surface problems too late to act on; a longer one would list
    /// certificates that are going to renew themselves tomorrow, and a number an operator learns to
    /// ignore is worse than no number.
    /// </remarks>
    private static readonly TimeSpan CertificateExpiryHorizon = TimeSpan.FromDays(30);

    /// <summary>Reads every attention source together; one that cannot be read marks only itself.</summary>
    /// <param name="bus">The bus every module query is dispatched through.</param>
    /// <param name="logger">Where a source whose query threw is recorded.</param>
    /// <param name="clock">The panel's clock, which the certificate horizon is measured from.</param>
    /// <param name="cancellationToken">Cancels every dispatch.</param>
    /// <returns>The five sources, each loaded or unavailable.</returns>
    public static async Task<DashboardAttentionDto> ReadAsync(
        IMessageBus bus,
        ILogger logger,
        IClock clock,
        CancellationToken cancellationToken)
    {
        // Together rather than one after another: the sources are independent, and the sum of five
        // round-trips would be the wait of every screen that asks.
        var certificates = DashboardSectionReader.ReadAsync<int>(
            bus,
            logger,
            new CountExpiringCertificatesQuery(clock.UtcNow.Add(CertificateExpiryHorizon)),
            cancellationToken);
        var failedBackups = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountFailedBackupsQuery(), cancellationToken);
        var bans = DashboardSectionReader.ReadAsync<int>(bus, logger, new CountBansQuery(), cancellationToken);
        var failedTasks = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountFailedTasksQuery(), cancellationToken);
        var clockSync = DashboardSectionReader.ReadAsync<ClockSyncDto>(
            bus, logger, new GetClockSyncQuery(), cancellationToken);

        await Task.WhenAll(certificates, failedBackups, bans, failedTasks, clockSync);

        return new DashboardAttentionDto(
            // Already-expired certificates are included, deliberately: the horizon is an upper
            // bound on "soon", and a certificate that lapsed yesterday has not stopped needing
            // attention by becoming late.
            CertificatesExpiringSoon: await certificates,
            FailedBackups: await failedBackups,
            BannedAddresses: await bans,
            FailedTasks: await failedTasks,
            Clock: await clockSync);
    }
}
