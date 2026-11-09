using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="PhpLimitsMapper"/>: four figures carried across, none inferred (issue #138).</summary>
public sealed class PhpLimitsMapperTests
{
    /// <summary>The bounds are restated on the wire figure for figure.</summary>
    [Fact]
    public void The_bounds_are_restated_on_the_wire_figure_for_figure()
    {
        // Four different figures, so a projection that fed a field from its neighbour fails here.
        var dto = PhpLimitsMapper.ToDto(PhpLimits.Of(101, 202, 103, 204));

        Assert.Equal(new PhpLimitsDto(101, 202, 103, 204), dto);
    }

    /// <summary>Wire bounds are read as the stored value figure for figure.</summary>
    [Fact]
    public void Wire_bounds_are_read_as_the_stored_value_figure_for_figure()
    {
        var limits = PhpLimitsMapper.ToDomain(new PhpLimitsDto(101, 202, 103, 204));

        Assert.Equal(PhpLimits.Of(101, 202, 103, 204), limits);
    }

    /// <summary>A figure the validator would have refused still cannot become a stored bound.</summary>
    [Fact]
    public void A_figure_the_validator_would_have_refused_cannot_become_a_stored_bound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PhpLimitsMapper.ToDomain(new PhpLimitsDto(0, 64, 120, 3000));
        });
    }

    /// <summary>The Sdk snapshot carries the four figures in their own slots.</summary>
    [Fact]
    public void The_sdk_snapshot_carries_the_four_figures_in_their_own_slots()
    {
        var snapshot = PhpLimitsMapper.ToSnapshot(PhpLimits.Of(101, 202, 103, 204));

        Assert.Equal(101, snapshot.MemoryLimitMaxMb);
        Assert.Equal(202, snapshot.UploadMaxMb);
        Assert.Equal(103, snapshot.MaxExecutionSeconds);
        Assert.Equal(204, snapshot.MaxInputVars);
    }
}
