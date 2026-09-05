using Maran.Modules.Firewall.Domain.Policies;

namespace Maran.Modules.Firewall.Tests.Common;

/// <summary>The ladder an automatic ban climbs as one address keeps coming back.</summary>
public sealed class BanTtlPolicyTests
{
    /// <summary>A first offence is banned for fifteen minutes.</summary>
    [Fact]
    public void A_first_offence_is_banned_for_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), BanTtlPolicy.ForPriorEpisodes(0));
    }

    /// <summary>A second offence is banned for an hour.</summary>
    [Fact]
    public void A_second_offence_is_banned_for_an_hour()
    {
        Assert.Equal(TimeSpan.FromHours(1), BanTtlPolicy.ForPriorEpisodes(1));
    }

    /// <summary>A third offence is banned for a day.</summary>
    [Fact]
    public void A_third_offence_is_banned_for_a_day()
    {
        Assert.Equal(TimeSpan.FromHours(24), BanTtlPolicy.ForPriorEpisodes(2));
    }

    /// <summary>Every further offence stays at a day.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(100)]
    public void Every_further_offence_stays_at_a_day(int priorEpisodes)
    {
        Assert.Equal(TimeSpan.FromHours(24), BanTtlPolicy.ForPriorEpisodes(priorEpisodes));
    }

    /// <summary>The ladder rises and never falls.</summary>
    [Fact]
    public void The_ladder_rises_and_never_falls()
    {
        // Asserted as a shape rather than as three numbers, so that a future retune which swapped
        // two rungs — the one change the three tests above would all still pass — goes red here.
        Assert.True(BanTtlPolicy.ForPriorEpisodes(0) < BanTtlPolicy.ForPriorEpisodes(1));
        Assert.True(BanTtlPolicy.ForPriorEpisodes(1) < BanTtlPolicy.ForPriorEpisodes(2));
    }
}
