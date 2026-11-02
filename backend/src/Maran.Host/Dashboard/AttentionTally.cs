using Maran.Agent.Client.Services.MonitorService;

namespace Maran.Host.Dashboard;

/// <summary>
/// Adds the attention sources up into the one figure the navigation badge shows.
/// </summary>
/// <remarks>
/// <para>
/// The figure is the number of things asking to be looked at: every certificate, failed backup,
/// banned address and failed task, plus one for a host clock that is anything but synchronised. The
/// clock is a fact and not a count, so it contributes one — the same single row the dashboard draws
/// for it.
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
                return DashboardSection.Loaded(
                    clock.State == AgentClockSyncState.Synchronized ? total : total + 1);
            },
            () =>
            {
                return DashboardSection.Unavailable<int>();
            });
    }
}
