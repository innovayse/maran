using Maran.Modules.Identity.Common;
using Maran.Modules.Monitoring.Common;

namespace Maran.Host.Dashboard;

/// <summary>What an administrator's landing screen shows: every server-wide section, each with its own read state.</summary>
/// <param name="Resources">The host's live resource reading.</param>
/// <param name="Services">The services the agent watches.</param>
/// <param name="Counts">What this server holds.</param>
/// <param name="Attention">What needs looking at.</param>
/// <param name="RecentAudit">The newest audit entries.</param>
public sealed record AdministratorDashboardDto(
    DashboardSection<HostMetricsDto> Resources,
    DashboardSection<IReadOnlyList<ServiceStatusDto>> Services,
    DashboardCountsDto Counts,
    DashboardAttentionDto Attention,
    DashboardSection<IReadOnlyList<AuditEventDto>> RecentAudit) : DashboardDto;
