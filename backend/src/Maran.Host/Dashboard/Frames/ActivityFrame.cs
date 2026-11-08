using Maran.Modules.Identity.Common;

namespace Maran.Host.Dashboard.Frames;

/// <summary>The newest audit entries, sent when they change.</summary>
/// <param name="RecentAudit">The entries, or unavailable when they could not be read.</param>
public sealed record ActivityFrame(DashboardSection<IReadOnlyList<AuditEventDto>> RecentAudit) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "activity";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
