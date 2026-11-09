using Maran.Modules.Monitoring.Common;

namespace Maran.Host.Dashboard.Frames;

/// <summary>The services the agent watches, sent when they change and checked on a slow cadence.</summary>
/// <param name="Services">The statuses, or unavailable when they could not be read — never an empty list.</param>
public sealed record ServicesFrame(DashboardSection<IReadOnlyList<ServiceStatusDto>> Services) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "services";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
