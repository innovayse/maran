namespace Maran.Modules.Monitoring.Common;

/// <summary>The alert thresholds as the settings screen shows and edits them (issue #142).</summary>
/// <param name="DiskPercent">The root filesystem alarms when it is fuller than this; always set.</param>
/// <param name="CpuPercent">The processor alarms when it is busier than this, or <c>null</c> when it is not watched.</param>
/// <param name="MemoryPercent">The memory alarms when more than this is in use, or <c>null</c> when it is not watched.</param>
/// <param name="MinimumPercent">The lowest value any threshold may take; the screen holds no constant of its own.</param>
/// <param name="MaximumPercent">The highest value any threshold may take.</param>
public sealed record AlertThresholdsDto(
    int DiskPercent,
    int? CpuPercent,
    int? MemoryPercent,
    int MinimumPercent,
    int MaximumPercent);
