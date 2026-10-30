using Maran.Modules.Identity.Common;
using Maran.Modules.Monitoring.Common;

namespace Maran.Host.Dashboard;

/// <summary>Everything the panel's landing screen shows, for the caller who asked.</summary>
/// <remarks>
/// <para>
/// <b>A null section means "not for this caller", and nothing else.</b> Both audiences receive this
/// one shape rather than two, so the SPA has a single contract and no second one to drift from: an
/// administrator receives every section, and everyone else receives every section null. The
/// difference is decided on the server, by <c>ICurrentUser.IsAdmin</c>, which fails closed.
/// </para>
/// <para>
/// <b>Whether a section could be read is its <see cref="DashboardSection{T}"/> state</b>, never an
/// empty list or a null (issue #248). An empty list is a real answer — no services watched, no
/// audit entries yet — and a different one from a module that did not answer, so the two are
/// carried as different values rather than drawn the same way.
/// </para>
/// </remarks>
/// <param name="IsAdministrator">
/// Whether the sections below were filled for this caller. Reported explicitly so the SPA renders
/// "nothing to show you here" rather than a screen of missing panels, and so a reader of the
/// payload can tell a withheld section from an absent one.
/// </param>
/// <param name="Resources">The host's live resource reading, or <c>null</c> when withheld.</param>
/// <param name="Services">The services the agent watches, or <c>null</c> when withheld.</param>
/// <param name="Counts">What this server holds, or <c>null</c> when withheld.</param>
/// <param name="Attention">What needs looking at, or <c>null</c> when withheld.</param>
/// <param name="RecentAudit">The newest audit entries, or <c>null</c> when withheld.</param>
public sealed record DashboardDto(
    bool IsAdministrator,
    DashboardSection<HostMetricsDto>? Resources,
    DashboardSection<IReadOnlyList<ServiceStatusDto>>? Services,
    DashboardCountsDto? Counts,
    DashboardAttentionDto? Attention,
    DashboardSection<IReadOnlyList<AuditEventDto>>? RecentAudit);
