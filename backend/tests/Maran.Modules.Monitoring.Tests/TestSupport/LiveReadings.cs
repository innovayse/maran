using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>Builds live readings for tests, so each test states only the figure it cares about.</summary>
public static class LiveReadings
{
    /// <summary>A reading at <paramref name="at"/> whose processor figure is <paramref name="cpuPercent"/>.</summary>
    /// <param name="at">When the reading was taken.</param>
    /// <param name="cpuPercent">The processor utilisation, which tests use to tell readings apart.</param>
    /// <returns>The reading, with unremarkable figures elsewhere.</returns>
    public static LiveMetricsReading At(DateTimeOffset at, double cpuPercent = 10)
    {
        return new LiveMetricsReading(
            at,
            new LiveCpuReading(cpuPercent, 0.5, 0.4, 0.3),
            new LiveMemoryReading(1_000, 8_000),
            new LiveDiskReading(2_000, 10_000),
            new LiveNetworkReading(100, 200));
    }
}
