using Maran.Host.Dashboard;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Dashboard;

/// <summary>Restating a live reading as the stream's <c>metrics</c> event: every figure carried, every absence kept.</summary>
public sealed class LiveMetricsFrameMapperTests
{
    /// <summary>A full reading maps every figure to its wire member.</summary>
    [Fact]
    public void A_full_reading_maps_every_figure_to_its_wire_member()
    {
        var at = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var reading = new LiveMetricsReading(
            at,
            new LiveCpuReading(42.5, 1.5, 1.2, 1.1),
            new LiveMemoryReading(3_000, 8_000),
            new LiveDiskReading(50, 100),
            new LiveNetworkReading(10.5, 20.5));

        var frame = LiveMetricsFrameMapper.ToFrame(reading);

        Assert.Equal(at, frame.T);
        Assert.Equal(42.5, frame.Cpu.Total);
        Assert.Equal(new double?[] { 1.5, 1.2, 1.1 }, frame.Cpu.Load);
        Assert.Equal(3_000, frame.Memory.Used);
        Assert.Equal(8_000, frame.Memory.Total);
        Assert.Equal(50, frame.Disk.Used);
        Assert.Equal(100, frame.Disk.Total);
        Assert.Equal(10.5, frame.Network.RxBps);
        Assert.Equal(20.5, frame.Network.TxBps);
    }

    /// <summary>A figure the reading did not record stays absent and is never turned into zero.</summary>
    [Fact]
    public void A_figure_the_reading_did_not_record_stays_absent_and_is_never_turned_into_zero()
    {
        var reading = new LiveMetricsReading(
            DateTimeOffset.UnixEpoch,
            new LiveCpuReading(1, 0.7, null, null),
            new LiveMemoryReading(1, 2),
            new LiveDiskReading(null, 100),
            new LiveNetworkReading(null, null));

        var frame = LiveMetricsFrameMapper.ToFrame(reading);

        Assert.Equal(new double?[] { 0.7, null, null }, frame.Cpu.Load);
        Assert.Null(frame.Disk.Used);
        Assert.Null(frame.Network.RxBps);
        Assert.Null(frame.Network.TxBps);
    }
}
