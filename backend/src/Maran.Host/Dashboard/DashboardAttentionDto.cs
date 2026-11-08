using Maran.Modules.Monitoring.Common;
using Maran.Modules.Notifications.Common;

namespace Maran.Host.Dashboard;

/// <summary>What on this server is asking to be looked at.</summary>
/// <remarks>
/// <para>
/// Five counts, each of a thing that is wrong NOW rather than a thing that once went wrong: a
/// certificate about to lapse, a backup that did not finish, an address the panel is refusing, a
/// panel task that failed, a site running a PHP version that no longer receives security fixes.
/// Every one of them is already visible on its own screen, and on none of them is it visible on
/// the screen an operator actually looks at.
/// </para>
/// <para>
/// Two more sources are not counts: the host clock, and the state of the channels an alert reaches
/// an operator through. An alert that cannot be delivered is the one problem that hides every other,
/// so it is listed here.
/// </para>
/// <para>
/// Zero is the ordinary answer, and the page renders it as a zero. An empty state here would read
/// as "not loaded" on the one screen where "nothing needs attention" is the good news.
/// </para>
/// <para>
/// <b>Each count carries whether its source could be read.</b> A module that did not answer is
/// <see cref="DashboardSectionState.Unavailable"/>, never zero: a zero there would join the others
/// in announcing that nothing needs attention, about a source nobody checked (issue #248). The SPA
/// names each unavailable source instead of drawing the all-clear.
/// </para>
/// </remarks>
/// <param name="CertificatesExpiringSoon">
/// Certificates whose expiry is within <c>AttentionReader.CertificateExpiryHorizon</c>.
/// </param>
/// <param name="FailedBackups">Backups whose last run failed.</param>
/// <param name="BannedAddresses">Addresses the firewall is currently refusing.</param>
/// <param name="FailedTasks">Panel tasks that ended in failure.</param>
/// <param name="SitesOnEndOfLifePhp">
/// SITES (not PHP versions) whose PHP version is past the end of its security support today, by the
/// panel's own lifecycle table (issue #157). A site on a version the table has no row for, and a site
/// with no PHP version, are not counted: unknown is not end of life.
/// </param>
/// <param name="Clock">
/// Whether the host's clock is synchronised with a time source (issue #150). Not a count: the SPA
/// names it when it is anything but synchronised, and names it as unchecked when the agent could
/// not be asked — never as synchronised.
/// </param>
/// <param name="AlertDelivery">
/// Whether the alert mail and the alert webhook reached their destination the last time the panel
/// tried (issue #142). Not a count: each failing channel adds one to the navigation figure and gets
/// its own row on the dashboard, and a delivery state that could not be read is unavailable, never
/// "delivering".
/// </param>
public sealed record DashboardAttentionDto(
    DashboardSection<int> CertificatesExpiringSoon,
    DashboardSection<int> FailedBackups,
    DashboardSection<int> BannedAddresses,
    DashboardSection<int> FailedTasks,
    DashboardSection<int> SitesOnEndOfLifePhp,
    DashboardSection<ClockSyncDto> Clock,
    DashboardSection<AlertDeliveryStatusDto> AlertDelivery);
