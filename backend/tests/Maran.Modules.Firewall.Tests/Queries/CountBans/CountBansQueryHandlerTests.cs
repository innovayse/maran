using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Queries.CountBans;
using Maran.Modules.Firewall.Queries.ListBans;
using Maran.Modules.Firewall.Tests.TestSupport;

namespace Maran.Modules.Firewall.Tests.Queries.CountBans;

/// <summary>Behavioral contract of <see cref="CountBansQueryHandler"/>.</summary>
public sealed class CountBansQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The count is of the bans in force, and equals the total the bans list reports.</summary>
    [Fact]
    public async Task The_count_is_of_the_bans_in_force_and_equals_the_lists_total()
    {
        // The landing screen's figure and the list it links to must not disagree: both read the
        // entity's in-force rule, so a lifted, an expired and an almost-expired episode are absent
        // from both.
        var lifted = Timed("192.0.2.3", Now.AddHours(1));
        lifted.Lift(Now.AddMinutes(-1));

        using var context = FirewallTestContext.Create();
        context.BanEpisodes.AddRange(
            Timed("203.0.113.7", Now.AddHours(1)),
            new BanEpisode(Guid.NewGuid(), "203.0.113.8", BanReason.Manual, null, 0, Now.AddDays(-1), null),
            Timed("198.51.100.9", Now.AddHours(-1)),
            Timed("198.51.100.10", Now.AddMilliseconds(500)),
            lifted);
        await context.SaveChangesAsync();
        var clock = new FakeClock(Now);

        var count = await new CountBansQueryHandler(context, clock)
            .HandleAsync(new CountBansQuery(), CancellationToken.None);
        var list = await new ListBansQueryHandler(context, clock)
            .HandleAsync(new ListBansQuery(), CancellationToken.None);

        Assert.True(count.IsSuccess);
        Assert.Equal(2, count.Value);
        Assert.Equal(list.Value.Total, count.Value);
    }

    /// <summary>Builds a timed episode expiring at <paramref name="expiresAt"/>.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="expiresAt">When the ban runs out.</param>
    private static BanEpisode Timed(string address, DateTimeOffset expiresAt)
    {
        return new BanEpisode(
            Guid.NewGuid(), address, BanReason.BruteForce, Now.AddHours(-2), 25, Now.AddHours(-2), expiresAt);
    }
}
