namespace Maran.Host.Dashboard;

/// <summary>What on this server is asking to be looked at.</summary>
/// <remarks>
/// <para>
/// Four counts, each of a thing that is wrong NOW rather than a thing that once went wrong: a
/// certificate about to lapse, a backup that did not finish, an address the panel is refusing, a
/// panel task that failed. Every one of them is already visible on its own screen, and on none of
/// them is it visible on the screen an operator actually looks at.
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
/// Certificates whose expiry is within <c>DashboardEndpoint.CertificateExpiryHorizon</c>.
/// </param>
/// <param name="FailedBackups">Backups whose last run failed.</param>
/// <param name="BannedAddresses">Addresses the firewall is currently refusing.</param>
/// <param name="FailedTasks">Panel tasks that ended in failure.</param>
public sealed record DashboardAttentionDto(
    DashboardSection<int> CertificatesExpiringSoon,
    DashboardSection<int> FailedBackups,
    DashboardSection<int> BannedAddresses,
    DashboardSection<int> FailedTasks);
