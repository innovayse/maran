using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;

namespace Maran.Modules.Firewall.Tests.Domain;

/// <summary>When a recorded ban is still in force, and how much of it is left to re-apply.</summary>
public sealed class BanEpisodeTests
{
    private static readonly DateTimeOffset BannedAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A timed episode is in force before its expiry.</summary>
    [Fact]
    public void A_timed_episode_is_in_force_before_its_expiry()
    {
        var episode = Timed(TimeSpan.FromHours(1));

        Assert.True(episode.IsInForce(BannedAt.AddMinutes(59)));
    }

    /// <summary>An episode whose expiry has passed is not in force.</summary>
    [Fact]
    public void An_episode_whose_expiry_has_passed_is_not_in_force()
    {
        var episode = Timed(TimeSpan.FromHours(1));

        Assert.False(episode.IsInForce(BannedAt.AddHours(2)));
    }

    /// <summary>An episode with less than a whole second left is already over.</summary>
    [Fact]
    public void An_episode_with_less_than_a_whole_second_left_is_already_over()
    {
        // The wire carries whole seconds and 0 there means PERMANENT, so a remainder of half a
        // second cannot be asked for as itself. Re-applying it would install the opposite of what
        // the row says.
        var episode = Timed(TimeSpan.FromHours(1));

        Assert.False(episode.IsInForce(BannedAt.AddHours(1).AddMilliseconds(-500)));
    }

    /// <summary>An episode with no expiry is in force until it is lifted.</summary>
    [Fact]
    public void An_episode_with_no_expiry_is_in_force_until_it_is_lifted()
    {
        var episode = Permanent();

        Assert.True(episode.IsInForce(BannedAt.AddYears(5)));

        episode.Lift(BannedAt.AddYears(5));

        Assert.False(episode.IsInForce(BannedAt.AddYears(5)));
    }

    /// <summary>The remaining ttl is what is left and never the original duration.</summary>
    [Fact]
    public void The_remaining_ttl_is_what_is_left_and_never_the_original_duration()
    {
        // A panel restarted twenty-three hours into a twenty-four-hour ban that re-applied the
        // original would hold the address for forty-seven hours — and a machine restarted often
        // enough would hold it forever, a permanent ban assembled out of temporary ones.
        var episode = Timed(TimeSpan.FromHours(24));

        Assert.Equal(TimeSpan.FromHours(1), episode.RemainingTtl(BannedAt.AddHours(23)));
    }

    /// <summary>A permanent episode has no remaining ttl at all.</summary>
    [Fact]
    public void A_permanent_episode_has_no_remaining_ttl_at_all()
    {
        // Null and not zero: zero is how the wire spells permanent, and a zero TimeSpan on this
        // side would be an expiring ban. The two call for opposite reconciliations.
        Assert.Null(Permanent().RemainingTtl(BannedAt.AddHours(1)));
    }

    /// <summary>Lifting an episode twice keeps the first instant.</summary>
    [Fact]
    public void Lifting_an_episode_twice_keeps_the_first_instant()
    {
        var episode = Permanent();

        episode.Lift(BannedAt.AddMinutes(5));
        episode.Lift(BannedAt.AddMinutes(50));

        Assert.Equal(BannedAt.AddMinutes(5), episode.LiftedAt);
    }

    /// <summary>An episode records the reason the agent will never hold.</summary>
    [Fact]
    public void An_episode_records_the_reason_the_agent_will_never_hold()
    {
        // The kernel holds an address and a countdown. Everything explaining a ban is here.
        var episode = Timed(TimeSpan.FromMinutes(15));

        Assert.Equal(BanReason.BruteForce, episode.Reason);
        Assert.Equal(25, episode.Failures);
    }

    /// <summary>Builds an episode that runs out after <paramref name="ttl"/>.</summary>
    /// <param name="ttl">How long the ban lasts.</param>
    private static BanEpisode Timed(TimeSpan ttl)
    {
        return new BanEpisode(
            Guid.NewGuid(), "203.0.113.7", BanReason.BruteForce, BannedAt, 25, BannedAt, BannedAt + ttl);
    }

    /// <summary>Builds an episode that lasts until somebody lifts it.</summary>
    private static BanEpisode Permanent()
    {
        return new BanEpisode(Guid.NewGuid(), "203.0.113.7", BanReason.Manual, null, 0, BannedAt, null);
    }
}
