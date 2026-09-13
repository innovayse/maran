namespace Maran.Modules.Monitoring.Domain.Entities;

/// <summary>
/// One reading of the host's resources, taken by the sampler roughly every sixty seconds and kept
/// for seven days (R10). The panel's charts are drawn from nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>A sample never changes.</b> It has no mutating method — not because none was needed yet, but
/// because a reading that could be edited would stop being a measurement. Every field is set once,
/// by the constructor, from one <c>GetHostMetrics</c> response.
/// </para>
/// <para>
/// <b>The two network figures are COUNTERS since the host booted, stored raw.</b> They are not
/// per-interval traffic and must never be charted as though they were: the agent holds no previous
/// sample and takes one reading. A rate is derived on read by dividing the difference between two
/// samples by the seconds that actually elapsed between them, and clamping a negative difference —
/// a reboot, an interface removed — to zero (R7). Storing a rate here instead would bake in an
/// assumed interval that the sampler is explicitly allowed to miss.
/// </para>
/// <para>
/// <b>There is no row for a sample that failed.</b> When the agent cannot be reached the sampler
/// writes nothing, so a gap in this table is exactly what it looks like: minutes the panel has no
/// numbers for. A row of zeroes would be a claim about the machine — no memory in use, no traffic,
/// no load — and the chart would draw it as one.
/// </para>
/// </remarks>
public sealed class MetricSample
{
    /// <summary>The row's identity. A database-generated sequence: nothing outside this table names a sample.</summary>
    public long Id { get; private set; }

    /// <summary>When the reading was taken, from <see cref="IClock"/> at the moment the agent answered.</summary>
    /// <remarks>
    /// The panel's clock rather than the host's, and the sampler's rather than the agent's, because
    /// this is the value every bucket boundary and every elapsed-seconds division is measured
    /// against — so it has to come from the one clock the tests can inject.
    /// </remarks>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Processor utilisation across all cores at the moment of the reading, 0.0-100.0.</summary>
    public double CpuPercent { get; private set; }

    /// <summary>Memory in use, in bytes.</summary>
    public long MemoryUsedBytes { get; private set; }

    /// <summary>Total installed memory, in bytes. Stored per sample because a host can be resized.</summary>
    public long MemoryTotalBytes { get; private set; }

    /// <summary>Disk space in use on the root filesystem, in bytes.</summary>
    public long DiskUsedBytes { get; private set; }

    /// <summary>Total capacity of the root filesystem, in bytes.</summary>
    public long DiskTotalBytes { get; private set; }

    /// <summary>Bytes received since the host booted — a counter, never an interval figure.</summary>
    public long NetworkRxBytes { get; private set; }

    /// <summary>Bytes sent since the host booted — a counter, never an interval figure.</summary>
    public long NetworkTxBytes { get; private set; }

    /// <summary>System load average over the last minute.</summary>
    /// <remarks>
    /// The one-minute figure alone. The agent reports all three, and the five- and fifteen-minute
    /// ones are averages OF this series — which the chart already computes when it buckets — so
    /// storing them would be storing the same information three times at three resolutions.
    /// </remarks>
    public double LoadAverage1m { get; private set; }

    /// <summary>Records one reading.</summary>
    /// <param name="capturedAt">When the reading was taken, from the panel's clock.</param>
    /// <param name="cpuPercent">Processor utilisation across all cores, 0.0-100.0.</param>
    /// <param name="memoryUsedBytes">Memory in use, in bytes.</param>
    /// <param name="memoryTotalBytes">Total installed memory, in bytes.</param>
    /// <param name="diskUsedBytes">Disk space in use on the root filesystem, in bytes.</param>
    /// <param name="diskTotalBytes">Total capacity of the root filesystem, in bytes.</param>
    /// <param name="networkRxBytes">Bytes received since boot, as the counter it is.</param>
    /// <param name="networkTxBytes">Bytes sent since boot, as the counter it is.</param>
    /// <param name="loadAverage1m">System load average over the last minute.</param>
    public MetricSample(
        DateTimeOffset capturedAt,
        double cpuPercent,
        long memoryUsedBytes,
        long memoryTotalBytes,
        long diskUsedBytes,
        long diskTotalBytes,
        long networkRxBytes,
        long networkTxBytes,
        double loadAverage1m)
    {
        CapturedAt = capturedAt;
        CpuPercent = cpuPercent;
        MemoryUsedBytes = memoryUsedBytes;
        MemoryTotalBytes = memoryTotalBytes;
        DiskUsedBytes = diskUsedBytes;
        DiskTotalBytes = diskTotalBytes;
        NetworkRxBytes = networkRxBytes;
        NetworkTxBytes = networkTxBytes;
        LoadAverage1m = loadAverage1m;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private MetricSample()
    {
    }
}
