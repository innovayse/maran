using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.Dashboard;
using Maran.Modules.Monitoring.Common;

namespace Maran.Host.Tests.Dashboard;

/// <summary>
/// Covers <see cref="AttentionTally"/>: the navigation's needs-attention figure is the sum of the
/// attention sources, and a source that could not be read makes the figure unavailable rather than
/// smaller.
/// </summary>
public sealed class AttentionTallyTests
{
    /// <summary>Every source read, none asking for attention, totals a loaded zero.</summary>
    /// <remarks>Zero is the ordinary answer and must stay an answer, not an unavailable figure.</remarks>
    [Fact]
    public void Sources_that_are_all_read_and_all_quiet_total_a_loaded_zero()
    {
        var total = AttentionTally.Total(Attention(0, 0, 0, 0, Synchronized()));

        Assert.Equal(DashboardSectionState.Loaded, total.State);
        Assert.Equal(0, ValueOf(total));
    }

    /// <summary>The four counts are added together.</summary>
    [Fact]
    public void The_four_counts_are_added_together()
    {
        var total = AttentionTally.Total(Attention(2, 3, 5, 7, Synchronized()));

        Assert.Equal(17, ValueOf(total));
    }

    /// <summary>A clock that is not synchronised adds exactly one, whichever way it is out.</summary>
    [Theory]
    [InlineData(AgentClockSyncState.NotSynchronized)]
    [InlineData(AgentClockSyncState.SyncServiceDisabled)]
    public void A_clock_that_is_not_synchronised_adds_one(AgentClockSyncState state)
    {
        var total = AttentionTally.Total(Attention(1, 0, 0, 0, DashboardSection.Loaded(new ClockSyncDto(state, "x"))));

        Assert.Equal(2, ValueOf(total));
    }

    /// <summary>
    /// A count source that could not be read makes the figure unavailable, even when the others add
    /// up to something: a smaller number would be a wrong one.
    /// </summary>
    [Fact]
    public void An_unread_count_source_makes_the_total_unavailable()
    {
        var attention = new DashboardAttentionDto(
            DashboardSection.Loaded(4),
            DashboardSection.Unavailable<int>(),
            DashboardSection.Loaded(1),
            DashboardSection.Loaded(1),
            Synchronized());

        var total = AttentionTally.Total(attention);

        Assert.Equal(DashboardSectionState.Unavailable, total.State);
    }

    /// <summary>A clock that could not be read makes the figure unavailable too.</summary>
    [Fact]
    public void An_unread_clock_makes_the_total_unavailable()
    {
        var total = AttentionTally.Total(Attention(1, 1, 1, 1, DashboardSection.Unavailable<ClockSyncDto>()));

        Assert.Equal(DashboardSectionState.Unavailable, total.State);
    }

    /// <summary>Builds an attention block from loaded counts and a clock section.</summary>
    /// <param name="certificates">Certificates expiring soon.</param>
    /// <param name="backups">Failed backups.</param>
    /// <param name="bans">Banned addresses.</param>
    /// <param name="tasks">Failed tasks.</param>
    /// <param name="clock">The clock section.</param>
    /// <returns>The block.</returns>
    private static DashboardAttentionDto Attention(
        int certificates,
        int backups,
        int bans,
        int tasks,
        DashboardSection<ClockSyncDto> clock)
    {
        return new DashboardAttentionDto(
            DashboardSection.Loaded(certificates),
            DashboardSection.Loaded(backups),
            DashboardSection.Loaded(bans),
            DashboardSection.Loaded(tasks),
            clock);
    }

    /// <summary>A clock section reporting a synchronised clock.</summary>
    /// <returns>The loaded section.</returns>
    private static DashboardSection<ClockSyncDto> Synchronized()
    {
        return DashboardSection.Loaded(new ClockSyncDto(AgentClockSyncState.Synchronized, "x"));
    }

    /// <summary>Reads the value of a section known to be loaded.</summary>
    /// <param name="section">The section.</param>
    /// <returns>Its value.</returns>
    private static int ValueOf(DashboardSection<int> section)
    {
        return section.Match(
            value =>
            {
                return value;
            },
            () =>
            {
                return -1;
            });
    }
}
