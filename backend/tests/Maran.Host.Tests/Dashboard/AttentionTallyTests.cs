using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.Dashboard;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Notifications.Common;

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

    /// <summary>The counts are added together.</summary>
    [Fact]
    public void The_counts_are_added_together()
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
            DashboardSection.Loaded(1),
            Synchronized(),
            Delivering());

        var total = AttentionTally.Total(attention);

        Assert.Equal(DashboardSectionState.Unavailable, total.State);
    }

    /// <summary>
    /// Sites on an end-of-life PHP version are added to the figure: the sixth count source reaches the
    /// badge as well as the dashboard block.
    /// </summary>
    [Fact]
    public void Sites_on_an_end_of_life_php_version_are_added_to_the_total()
    {
        var total = AttentionTally.Total(Attention(2, 3, 5, 7, Synchronized(), endOfLifeSites: 11));

        Assert.Equal(28, ValueOf(total));
    }

    /// <summary>
    /// An end-of-life source that could not be read makes the figure unavailable even when every other
    /// source adds up to something: all or nothing, so a lower figure is never shown for a host whose
    /// PHP versions nobody checked.
    /// </summary>
    [Fact]
    public void An_unread_end_of_life_php_source_makes_the_total_unavailable()
    {
        var attention = new DashboardAttentionDto(
            DashboardSection.Loaded(4),
            DashboardSection.Loaded(1),
            DashboardSection.Loaded(1),
            DashboardSection.Loaded(1),
            DashboardSection.Unavailable<int>(),
            Synchronized(),
            Delivering());

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

    /// <summary>A channel whose last delivery failed adds one each, and one that is fine or unknown adds nothing.</summary>
    [Theory]
    [InlineData("ok", "ok", 0)]
    [InlineData("unknown", "unknown", 0)]
    [InlineData("failing", "ok", 1)]
    [InlineData("ok", "failing", 1)]
    [InlineData("failing", "failing", 2)]
    public void Each_failing_alert_channel_adds_one(string mail, string webhook, int expected)
    {
        var attention = Attention(0, 0, 0, 0, Synchronized(), Delivery(mail, webhook));

        Assert.Equal(expected, ValueOf(AttentionTally.Total(attention)));
    }

    /// <summary>A delivery state that could not be read makes the figure unavailable, not smaller.</summary>
    [Fact]
    public void An_unread_alert_delivery_state_makes_the_total_unavailable()
    {
        var attention = Attention(
            1, 1, 1, 1, Synchronized(), DashboardSection.Unavailable<AlertDeliveryStatusDto>());

        Assert.Equal(DashboardSectionState.Unavailable, AttentionTally.Total(attention).State);
    }

    /// <summary>Builds an attention block from loaded counts and a clock section.</summary>
    /// <param name="certificates">Certificates expiring soon.</param>
    /// <param name="backups">Failed backups.</param>
    /// <param name="bans">Banned addresses.</param>
    /// <param name="tasks">Failed tasks.</param>
    /// <param name="clock">The clock section.</param>
    /// <param name="alertDelivery">The alert delivery section; both channels delivering when omitted.</param>
    /// <param name="endOfLifeSites">Sites on an end-of-life PHP version.</param>
    /// <returns>The block.</returns>
    private static DashboardAttentionDto Attention(
        int certificates,
        int backups,
        int bans,
        int tasks,
        DashboardSection<ClockSyncDto> clock,
        DashboardSection<AlertDeliveryStatusDto>? alertDelivery = null,
        int endOfLifeSites = 0)
    {
        return new DashboardAttentionDto(
            DashboardSection.Loaded(certificates),
            DashboardSection.Loaded(backups),
            DashboardSection.Loaded(bans),
            DashboardSection.Loaded(tasks),
            DashboardSection.Loaded(endOfLifeSites),
            clock,
            alertDelivery ?? Delivering());
    }

    /// <summary>An alert delivery section reporting both channels fine.</summary>
    /// <returns>The loaded section.</returns>
    private static DashboardSection<AlertDeliveryStatusDto> Delivering()
    {
        return Delivery("ok", "ok");
    }

    /// <summary>An alert delivery section with the given channel states.</summary>
    /// <param name="mail">The mail channel's state.</param>
    /// <param name="webhook">The webhook channel's state.</param>
    /// <returns>The loaded section.</returns>
    private static DashboardSection<AlertDeliveryStatusDto> Delivery(string mail, string webhook)
    {
        return DashboardSection.Loaded(new AlertDeliveryStatusDto(
            new AlertChannelStatusDto(mail, null, null),
            new AlertChannelStatusDto(webhook, null, null)));
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
