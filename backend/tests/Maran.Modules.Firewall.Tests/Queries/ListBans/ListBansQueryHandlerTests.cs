using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Queries.ListBans;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Firewall.Tests.Queries.ListBans;

/// <summary>Which bans the panel shows, in what order, and where it reads them from.</summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; <c>FirewallListEndpointTests</c> covers it against a real server.
/// </remarks>
public sealed class ListBansQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A listing shows only the bans still in force.</summary>
    [Fact]
    public async Task A_listing_shows_only_the_bans_still_in_force()
    {
        var lifted = Timed("192.0.2.3", Now.AddHours(1));
        lifted.Lift(Now.AddMinutes(-1));

        using var context = FirewallTestContext.Create();
        context.BanEpisodes.AddRange(
            Timed("203.0.113.7", Now.AddHours(1)),
            Timed("198.51.100.9", Now.AddHours(-1)),
            lifted);
        await context.SaveChangesAsync();

        var result = await new ListBansQueryHandler(context, new FakeClock(Now))
            .HandleAsync(new ListBansQuery(), CancellationToken.None);

        Assert.Equal(["203.0.113.7"], result.Value.Items.Select(ban =>
        {
            return ban.IpAddress;
        }));
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>A listing carries the reason the agent could never hold.</summary>
    [Fact]
    public async Task A_listing_carries_the_reason_the_agent_could_never_hold()
    {
        // What the kernel holds is an address and a countdown. The column an operator opens this
        // screen for is why — and the agent stores none, because the only place one could go there
        // is an nftables comment whose argument nft parses in its own grammar.
        using var context = FirewallTestContext.Create();
        context.BanEpisodes.Add(Timed("203.0.113.7", Now.AddHours(1)));
        await context.SaveChangesAsync();

        var result = await new ListBansQueryHandler(context, new FakeClock(Now))
            .HandleAsync(new ListBansQuery(), CancellationToken.None);

        var ban = Assert.Single(result.Value.Items);
        Assert.Equal(BanReason.BruteForce, ban.Reason);
        Assert.Equal(25, ban.Failures);
        Assert.Equal(Now.AddHours(1), ban.ExpiresAt);
    }

    /// <summary>The newest ban comes first, and the page is cut after the in-force rule, not before it.</summary>
    [Fact]
    public async Task The_newest_ban_comes_first_and_the_page_is_cut_after_the_in_force_rule()
    {
        // The episode with half a second left is excluded by the entity's rule; were that rule applied
        // after the page was cut, the first page would hold one row fewer than it should and the total
        // would count a row nobody is shown.
        using var context = FirewallTestContext.Create();
        context.BanEpisodes.AddRange(
            TimedAt("203.0.113.1", Now.AddMinutes(-1), Now.AddHours(1)),
            TimedAt("203.0.113.2", Now.AddMinutes(-2), Now.AddMilliseconds(500)),
            TimedAt("203.0.113.3", Now.AddMinutes(-3), Now.AddHours(1)),
            TimedAt("203.0.113.4", Now.AddMinutes(-4), Now.AddHours(1)));
        await context.SaveChangesAsync();

        var result = await new ListBansQueryHandler(context, new FakeClock(Now))
            .HandleAsync(new ListBansQuery(PageSize: 10), CancellationToken.None);

        Assert.Equal(["203.0.113.1", "203.0.113.3", "203.0.113.4"], result.Value.Items.Select(ban =>
        {
            return ban.IpAddress;
        }));
        Assert.Equal(3, result.Value.Total);
    }

    /// <summary>A named sort orders the page, and the opposite direction reverses it.</summary>
    [Fact]
    public async Task A_named_sort_orders_the_page_and_the_opposite_direction_reverses_it()
    {
        using var context = FirewallTestContext.Create();
        context.BanEpisodes.AddRange(
            TimedAt("203.0.113.1", Now.AddMinutes(-1), Now.AddHours(3)),
            TimedAt("203.0.113.2", Now.AddMinutes(-2), Now.AddHours(1)),
            TimedAt("203.0.113.3", Now.AddMinutes(-3), Now.AddHours(2)));
        await context.SaveChangesAsync();
        var handler = new ListBansQueryHandler(context, new FakeClock(Now));

        var soonest = await handler.HandleAsync(
            new ListBansQuery(Sort: SortRequest.Parse(BanSortFields.ExpiresAt)), CancellationToken.None);
        var latest = await handler.HandleAsync(
            new ListBansQuery(Sort: SortRequest.Parse("-" + BanSortFields.ExpiresAt)), CancellationToken.None);

        Assert.Equal(["203.0.113.2", "203.0.113.3", "203.0.113.1"], soonest.Value.Items.Select(ban =>
        {
            return ban.IpAddress;
        }));
        Assert.Equal(["203.0.113.1", "203.0.113.3", "203.0.113.2"], latest.Value.Items.Select(ban =>
        {
            return ban.IpAddress;
        }));
    }

    /// <summary>The ban listing cannot reach the agent at all.</summary>
    [Fact]
    public void The_ban_listing_cannot_reach_the_agent_at_all()
    {
        // Asserted against the constructor rather than by handing the handler an agent double and
        // finding it unused — a double nothing can call proves nothing (rules/testing.md). What is
        // being defended is that the kernel is not a source for this screen: its ban listing is an
        // address and a countdown, missing the one column the screen exists for. Adding the agent
        // client to this handler turns this red, which is the point.
        var parameters = typeof(ListBansQueryHandler).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parameters, parameter =>
        {
            return typeof(IAgentFirewallClient).IsAssignableFrom(parameter.ParameterType);
        });
    }

    /// <summary>Builds a timed episode placed at <paramref name="bannedAt"/>.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the ban was placed.</param>
    /// <param name="expiresAt">When the ban runs out.</param>
    private static BanEpisode TimedAt(string address, DateTimeOffset bannedAt, DateTimeOffset expiresAt)
    {
        return new BanEpisode(Guid.NewGuid(), address, BanReason.BruteForce, bannedAt, 25, bannedAt, expiresAt);
    }

    /// <summary>Builds a timed episode expiring at <paramref name="expiresAt"/>.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="expiresAt">When the ban runs out.</param>
    private static BanEpisode Timed(string address, DateTimeOffset expiresAt)
    {
        return new BanEpisode(
            Guid.NewGuid(),
            address,
            BanReason.BruteForce,
            Now.AddHours(-2),
            25,
            Now.AddHours(-2),
            expiresAt);
    }
}
