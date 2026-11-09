using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Notifications.Common;

namespace Maran.Host.Dashboard;

/// <summary>
/// Adds the attention sources up into the one figure the navigation badge shows.
/// </summary>
/// <remarks>
/// <para>
/// The figure is the number of things asking to be looked at: every certificate, failed backup,
/// banned address, failed task and site on an end-of-life PHP version, plus one for a host clock that is anything but synchronised. The
/// clock is a fact and not a count, so it contributes one — the same single row the dashboard draws
/// for it. So is each alert channel (mail, webhook) whose last delivery failed: one for each, the
/// same row the dashboard draws for it.
/// </para>
/// <para>
/// <b>A badge is all or nothing.</b> When any source could not be read the total is unavailable, not
/// the sum of the sources that could: a lower figure is a wrong figure, and the badge would read
/// "nothing much" about a host whose backups nobody checked (issue #248 is the same defect on the
/// dashboard). The dashboard still names the unread source; the badge, which has no room to, shows
/// nothing rather than less than the truth.
/// </para>
/// </remarks>
public static class AttentionTally
{
    /// <summary>
    /// The state a channel reports when its last delivery failed, as the Notifications module spells
    /// it on the wire (<c>ok</c>, <c>failing</c>, <c>unknown</c>).
    /// </summary>
    /// <remarks>
    /// <c>unknown</c> (no alert has been attempted on that channel) is NOT counted: a fresh install
    /// has delivered nothing and failed nothing, and a badge raised by silence would be raised on
    /// every new server.
    /// </remarks>
    private const string FailingChannelState = "failing";

    /// <summary>Adds the sources up.</summary>
    /// <param name="attention">The sources, as <see cref="AttentionReader"/> read them.</param>
    /// <returns>The total, or unavailable when any source could not be read.</returns>
    public static DashboardSection<int> Total(DashboardAttentionDto attention)
    {
        var counts = new[]
        {
            attention.CertificatesExpiringSoon,
            attention.FailedBackups,
            attention.BannedAddresses,
            attention.FailedTasks,
            attention.SitesOnEndOfLifePhp,
        };
        var total = 0;
        foreach (var count in counts)
        {
            if (count.State != DashboardSectionState.Loaded)
            {
                return DashboardSection.Unavailable<int>();
            }

            total += count.Match(
                value =>
                {
                    return value;
                },
                () =>
                {
                    return 0;
                });
        }

        return attention.Clock.Match(
            clock =>
            {
                var withClock = clock.State == AgentClockSyncState.Synchronized ? total : total + 1;
                return attention.AlertDelivery.Match(
                    delivery =>
                    {
                        return DashboardSection.Loaded(withClock + FailingChannels(delivery));
                    },
                    () =>
                    {
                        return DashboardSection.Unavailable<int>();
                    });
            },
            () =>
            {
                return DashboardSection.Unavailable<int>();
            });
    }

    /// <summary>Counts the alert channels whose last delivery failed.</summary>
    /// <param name="delivery">The state of both channels.</param>
    /// <returns>0, 1 or 2.</returns>
    private static int FailingChannels(AlertDeliveryStatusDto delivery)
    {
        var failing = 0;
        if (delivery.Mail.State == FailingChannelState)
        {
            failing++;
        }

        if (delivery.Webhook.State == FailingChannelState)
        {
            failing++;
        }

        return failing;
    }
}
