using Maran.Modules.Identity.Common;
using Maran.Modules.Monitoring.Common;

namespace Maran.Host.Dashboard;

/// <summary>Everything the panel's landing screen shows, for the caller who asked.</summary>
/// <remarks>
/// <para>
/// <b>A null section means "not for this caller", never "none" and never "not loaded yet".</b> Both
/// audiences receive this one shape rather than two, so the SPA has a single contract and no second
/// one to drift from: an administrator receives every section filled, and everyone else receives
/// every administrator-only section null. The difference is decided on the server, by
/// <c>ICurrentUser.IsAdmin</c>, which fails closed.
/// </para>
/// <para>
/// A section that is PRESENT but empty is a real answer and a different one: no services watched,
/// no audit entries yet. That is why the two list sections are non-nullable and simply arrive empty
/// for a caller who may not see them — an empty list and a withheld list look the same to a
/// customer, and the sections that carry numbers are the ones where the distinction matters.
/// </para>
/// </remarks>
/// <param name="IsAdministrator">
/// Whether the sections below were filled for this caller. Reported explicitly so the SPA renders
/// "nothing to show you here" rather than a screen of missing panels, and so a reader of the
/// payload can tell a withheld section from an absent one.
/// </param>
/// <param name="Resources">The host's live resource reading, or <c>null</c> when withheld or unreadable.</param>
/// <param name="Services">The services the agent watches; empty when withheld or unreadable.</param>
/// <param name="Counts">What this server holds, or <c>null</c> when withheld or unreadable.</param>
/// <param name="Attention">What needs looking at, or <c>null</c> when withheld or unreadable.</param>
/// <param name="RecentAudit">The newest audit entries; empty when withheld or unreadable.</param>
public sealed record DashboardDto(
    bool IsAdministrator,
    HostMetricsDto? Resources,
    IReadOnlyList<ServiceStatusDto> Services,
    DashboardCountsDto? Counts,
    DashboardAttentionDto? Attention,
    IReadOnlyList<AuditEventDto> RecentAudit);
