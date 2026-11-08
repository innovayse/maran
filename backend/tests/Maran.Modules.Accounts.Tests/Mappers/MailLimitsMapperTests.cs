using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="MailLimitsMapper"/>: four figures carried across, none inferred (issue #177).</summary>
public sealed class MailLimitsMapperTests
{
    /// <summary>The figures are restated on the wire figure for figure.</summary>
    [Fact]
    public void The_figures_are_restated_on_the_wire_figure_for_figure()
    {
        // Four different figures, so a projection that fed a field from its neighbour fails here.
        var dto = MailLimitsMapper.ToDto(MailLimits.Of(11, 202, 33, 4_404));

        Assert.Equal(new MailLimitsDto(11, 202, 33, 4_404), dto);
    }

    /// <summary>Wire figures are read as the stored value figure for figure.</summary>
    [Fact]
    public void Wire_figures_are_read_as_the_stored_value_figure_for_figure()
    {
        var limits = MailLimitsMapper.ToDomain(new MailLimitsDto(11, 202, 33, 4_404));

        Assert.Equal(MailLimits.Of(11, 202, 33, 4_404), limits);
    }

    /// <summary>A figure the validator would have refused still cannot become a stored figure.</summary>
    [Fact]
    public void A_figure_the_validator_would_have_refused_cannot_become_a_stored_figure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            MailLimitsMapper.ToDomain(new MailLimitsDto(10, 0, 100, 10_240));
        });
    }

    /// <summary>The Sdk snapshot carries the four figures in their own slots.</summary>
    [Fact]
    public void The_sdk_snapshot_carries_the_four_figures_in_their_own_slots()
    {
        var snapshot = MailLimitsMapper.ToSnapshot(MailLimits.Of(11, 202, 33, 4_404));

        Assert.Equal(11, snapshot.MaxMailboxes);
        Assert.Equal(202, snapshot.DefaultMailboxQuotaMib);
        Assert.Equal(33, snapshot.MailboxSendLimitPerHour);
        Assert.Equal(4_404, snapshot.MailboxQuotaPoolMib);
    }
}
