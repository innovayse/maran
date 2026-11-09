using Maran.Modules.Monitoring.Domain.Entities;

namespace Maran.Modules.Monitoring.Tests.Domain.Entities;

/// <summary>The alert thresholds: what they start as, and the bounds they refuse to leave.</summary>
public sealed class AlertThresholdsTests
{
    /// <summary>The instant every fixture stamps the thresholds with.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A panel nobody configured watches the disk at ninety and leaves the processor and memory off.</summary>
    [Fact]
    public void The_defaults_watch_the_disk_at_ninety_and_leave_processor_and_memory_off()
    {
        var thresholds = AlertThresholds.CreateDefault(Now);

        Assert.Equal(AlertThresholds.SingletonId, thresholds.Id);
        Assert.Equal(90, thresholds.DiskPercent);
        Assert.Null(thresholds.CpuPercent);
        Assert.Null(thresholds.MemoryPercent);
    }

    /// <summary>Replacing stores all three figures and the instant.</summary>
    [Fact]
    public void Replacing_stores_all_three_figures_and_the_instant()
    {
        var thresholds = AlertThresholds.CreateDefault(Now);

        thresholds.Replace(75, 80, null, Now.AddHours(1));

        Assert.Equal(75, thresholds.DiskPercent);
        Assert.Equal(80, thresholds.CpuPercent);
        Assert.Null(thresholds.MemoryPercent);
        Assert.Equal(Now.AddHours(1), thresholds.UpdatedAt);
    }

    /// <summary>Both ends of the bounds are accepted: the inverse control of the refusals below.</summary>
    [Theory]
    [InlineData(50)]
    [InlineData(99)]
    public void Both_ends_of_the_bounds_are_accepted(int percent)
    {
        var thresholds = AlertThresholds.CreateDefault(Now);

        thresholds.Replace(percent, percent, percent, Now);

        Assert.Equal(percent, thresholds.DiskPercent);
        Assert.Equal(percent, thresholds.CpuPercent);
        Assert.Equal(percent, thresholds.MemoryPercent);
    }

    /// <summary>A figure just outside the bounds is refused for each of the three thresholds, and nothing changes.</summary>
    [Theory]
    [InlineData(49, 80, 80)]
    [InlineData(100, 80, 80)]
    [InlineData(80, 49, 80)]
    [InlineData(80, 100, 80)]
    [InlineData(80, 80, 49)]
    [InlineData(80, 80, 100)]
    public void A_figure_outside_the_bounds_is_refused_and_nothing_changes(int disk, int cpu, int memory)
    {
        var thresholds = AlertThresholds.CreateDefault(Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            thresholds.Replace(disk, cpu, memory, Now.AddHours(1));
        });

        Assert.Equal(90, thresholds.DiskPercent);
        Assert.Null(thresholds.CpuPercent);
        Assert.Null(thresholds.MemoryPercent);
        Assert.Equal(Now, thresholds.UpdatedAt);
    }

    /// <summary>The bounds check the validator shares answers true inside and false outside.</summary>
    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(99, true)]
    [InlineData(100, false)]
    public void The_shared_bounds_check_agrees_with_the_bounds(int percent, bool expected)
    {
        Assert.Equal(expected, AlertThresholds.IsWithinBounds(percent));
    }
}
