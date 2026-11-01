using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Models;

namespace Maran.Modules.Monitoring.Tests.Domain.Policies;

/// <summary>Where the free-space floor's warning margin starts and ends (issue #148).</summary>
public sealed class FreeSpaceFloorMarginTests
{
    /// <summary>One gibibyte, the installer's default floor.</summary>
    private const ulong Floor = 1024UL * 1024 * 1024;

    /// <summary>One byte under twice the floor is inside the margin.</summary>
    [Fact]
    public void One_byte_under_twice_the_floor_is_approaching()
    {
        Assert.True(FreeSpaceFloorMargin.IsApproaching(new FreeSpaceFloorReading((2 * Floor) - 1, Floor)));
    }

    /// <summary>Exactly twice the floor is clear: the inverse control of the test above.</summary>
    [Fact]
    public void Exactly_twice_the_floor_is_clear()
    {
        Assert.False(FreeSpaceFloorMargin.IsApproaching(new FreeSpaceFloorReading(2 * Floor, Floor)));
    }

    /// <summary>Below the floor itself is, of course, still approaching — the alert stays raised.</summary>
    [Fact]
    public void Below_the_floor_is_still_approaching()
    {
        Assert.True(FreeSpaceFloorMargin.IsApproaching(new FreeSpaceFloorReading(0, Floor)));
    }

    /// <summary>A floor near the top of the range does not overflow into a small one that silences the alert.</summary>
    [Fact]
    public void A_floor_near_the_largest_value_does_not_overflow()
    {
        Assert.True(FreeSpaceFloorMargin.IsApproaching(new FreeSpaceFloorReading(ulong.MaxValue - 1, ulong.MaxValue)));
    }
}
