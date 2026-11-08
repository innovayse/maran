namespace Maran.Host.Dashboard.Frames;

/// <summary>One host reading. Sent every tick, and as the elements of a <see cref="HistoryFrame"/>.</summary>
/// <param name="T">When the reading was taken, from the panel's clock.</param>
/// <param name="Cpu">Processor utilisation and load.</param>
/// <param name="Memory">Memory in use and installed.</param>
/// <param name="Disk">The root filesystem.</param>
/// <param name="Network">Traffic rates.</param>
public sealed record MetricsFrame(
    DateTimeOffset T,
    MetricsCpuDto Cpu,
    MetricsMemoryDto Memory,
    MetricsDiskDto Disk,
    MetricsNetworkDto Network) : DashboardStreamFrame
{
    /// <summary>The event name.</summary>
    public const string Name = "metrics";

    /// <inheritdoc />
    public override string EventName()
    {
        return Name;
    }
}
