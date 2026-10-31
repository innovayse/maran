using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Queries.GetHostMetrics;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.GetHostMetrics;

/// <summary>The live reading as the panel hands it out: every disk figure in its own member.</summary>
public sealed class GetHostMetricsQueryHandlerTests
{
    /// <summary>
    /// The three disk figures reach the DTO unaltered and in their own members, and they sum to the size.
    /// </summary>
    /// <remarks>
    /// The issue's own host: a 97.9 GiB ext4 root on which <c>df</c> said 7.7 GiB used while the panel
    /// said 12.7 GiB, the difference being the 5% root reserve. Four different values, so a projection
    /// that swapped two of them cannot pass on equal numbers.
    /// </remarks>
    [Fact]
    public async Task The_three_disk_figures_reach_the_dto_in_their_own_members_and_sum_to_the_size()
    {
        const ulong used = 8_267_812_864;
        const ulong reserved = 5_253_365_760;
        const ulong available = 91_591_696_384;
        const ulong total = used + reserved + available;
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Ok(
                new AgentHostMetrics(1, 2, 3, used, reserved, available, total, 4, 5, 0, 0, 0)),
        };

        var result = await new GetHostMetricsQueryHandler(agent).HandleAsync(
            new GetHostMetricsQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((long)used, result.Value.DiskUsedBytes);
        Assert.Equal((long)reserved, result.Value.DiskReservedBytes);
        Assert.Equal((long)available, result.Value.DiskAvailableBytes);
        Assert.Equal((long)total, result.Value.DiskTotalBytes);
        Assert.Equal(
            result.Value.DiskTotalBytes,
            result.Value.DiskUsedBytes + result.Value.DiskReservedBytes + result.Value.DiskAvailableBytes);
    }

    /// <summary>A filesystem that keeps no reserve, such as xfs, reports a reserved figure of zero.</summary>
    [Fact]
    public async Task A_filesystem_that_keeps_no_reserve_reports_a_reserved_figure_of_zero()
    {
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Ok(new AgentHostMetrics(1, 2, 3, 300, 0, 700, 1_000, 4, 5, 0, 0, 0)),
        };

        var result = await new GetHostMetricsQueryHandler(agent).HandleAsync(
            new GetHostMetricsQuery(),
            CancellationToken.None);

        Assert.Equal(0, result.Value.DiskReservedBytes);
        Assert.Equal(1_000, result.Value.DiskUsedBytes + result.Value.DiskAvailableBytes);
    }

    /// <summary>A byte count above the signed ceiling saturates rather than turning negative.</summary>
    [Fact]
    public async Task A_disk_figure_above_the_signed_ceiling_saturates_rather_than_turning_negative()
    {
        var agent = new StubAgentMonitorClient
        {
            Metrics = Result<AgentHostMetrics>.Ok(
                new AgentHostMetrics(1, 2, 3, 1, 1, ulong.MaxValue, ulong.MaxValue, 4, 5, 0, 0, 0)),
        };

        var result = await new GetHostMetricsQueryHandler(agent).HandleAsync(
            new GetHostMetricsQuery(),
            CancellationToken.None);

        Assert.Equal(long.MaxValue, result.Value.DiskAvailableBytes);
        Assert.Equal(long.MaxValue, result.Value.DiskTotalBytes);
    }
}
