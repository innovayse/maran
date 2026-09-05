using Maran.Agent.Client.Interfaces;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Queries.ListBans;
using Maran.Modules.Firewall.Tests.TestSupport;

namespace Maran.Modules.Firewall.Tests.Queries.ListBans;

/// <summary>Which bans the panel shows, and where it reads them from.</summary>
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

        Assert.Equal(["203.0.113.7"], result.Value.Select(ban =>
        {
            return ban.IpAddress;
        }));
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

        var ban = Assert.Single(result.Value);
        Assert.Equal(BanReason.BruteForce, ban.Reason);
        Assert.Equal(25, ban.Failures);
        Assert.Equal(Now.AddHours(1), ban.ExpiresAt);
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
