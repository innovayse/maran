using Maran.Host.Dashboard.Frames;
using Maran.Sdk.Contracts;

namespace Maran.Host.Dashboard;

/// <summary>Restates a live host reading as the <c>metrics</c> event the stream sends.</summary>
/// <remarks>
/// A translation and nothing else: every member the reading did not record stays absent on the wire.
/// This is the one place the wire names (<c>t</c>, <c>rxBps</c>, ...) meet the Sdk's, so the live
/// frames and the back-fill are the same shape by construction.
/// </remarks>
public static class LiveMetricsFrameMapper
{
    /// <summary>Maps one reading.</summary>
    /// <param name="reading">The reading from the shared feed.</param>
    /// <returns>The event for it.</returns>
    public static MetricsFrame ToFrame(LiveMetricsReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new MetricsFrame(
            reading.At,
            new MetricsCpuDto(
                reading.Cpu.TotalPercent,
                [reading.Cpu.LoadAverage1m, reading.Cpu.LoadAverage5m, reading.Cpu.LoadAverage15m]),
            new MetricsMemoryDto(reading.Memory.UsedBytes, reading.Memory.TotalBytes),
            new MetricsDiskDto(reading.Disk.UsedBytes, reading.Disk.TotalBytes),
            new MetricsNetworkDto(reading.Network.ReceiveBytesPerSecond, reading.Network.SendBytesPerSecond));
    }
}
