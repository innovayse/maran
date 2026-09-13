using Maran.Modules.Firewall.Commands.UnbanAddress;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Firewall.Tests.Commands.UnbanAddress;

/// <summary>What lifting a ban changes, and what it refuses to change.</summary>
public sealed class UnbanAddressCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Lifting a ban marks the episode and tells the agent.</summary>
    [Fact]
    public async Task Lifting_a_ban_marks_the_episode_and_tells_the_agent()
    {
        var world = new World(InForce("203.0.113.7"));

        var result = await world.UnbanAsync("203.0.113.7");

        Assert.True(result.IsSuccess);
        Assert.Equal(["203.0.113.7"], world.Agent.Unbans);
        Assert.Equal(Now, Assert.Single(await world.EpisodesAsync()).LiftedAt);
    }

    /// <summary>Lifting an address lifts every episode in force for it.</summary>
    [Fact]
    public async Task Lifting_an_address_lifts_every_episode_in_force_for_it()
    {
        // An address may carry a manual ban and an automatic one at once, and leaving either behind
        // means the customer still cannot reach the server after being told they can.
        var world = new World(InForce("203.0.113.7"), InForce("203.0.113.7"));

        await world.UnbanAsync("203.0.113.7");

        Assert.All(await world.EpisodesAsync(), episode =>
        {
            Assert.NotNull(episode.LiftedAt);
        });
    }

    /// <summary>A mapped address lifts the ban recorded in its plain form.</summary>
    [Fact]
    public async Task A_mapped_address_lifts_the_ban_recorded_in_its_plain_form()
    {
        var world = new World(InForce("203.0.113.7"));

        var result = await world.UnbanAsync("::ffff:203.0.113.7");

        Assert.True(result.IsSuccess);
        Assert.Equal(["203.0.113.7"], world.Agent.Unbans);
    }

    /// <summary>Unbanning an address with no ban answers not found and never touches the agent.</summary>
    [Fact]
    public async Task Unbanning_an_address_with_no_ban_answers_not_found_and_never_touches_the_agent()
    {
        var world = new World();

        var result = await world.UnbanAsync("203.0.113.7");

        Assert.False(result.IsSuccess);
        Assert.Equal("BanNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Unbans);
    }

    /// <summary>An expired episode is not something that can be unbanned.</summary>
    [Fact]
    public async Task An_expired_episode_is_not_something_that_can_be_unbanned()
    {
        var world = new World(Expired("203.0.113.7"));

        var result = await world.UnbanAsync("203.0.113.7");

        Assert.Equal("BanNotFound", result.Error!.Code);
    }

    /// <summary>An agent that reports no such ban still releases the panels row.</summary>
    [Fact]
    public async Task An_agent_that_reports_no_such_ban_still_releases_the_panels_row()
    {
        // The case this branch exists for: a machine restarted before the reconciler ran holds no
        // ban in the kernel while the row still says the address is banned. Refusing here would
        // leave an address the administrator has just released permanently unreleasable — and the
        // next reconciliation pass would re-apply it.
        var world = new World(InForce("203.0.113.7"));
        world.Agent.UnbanResult = Result<bool>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.UnbanAsync("203.0.113.7");

        Assert.True(result.IsSuccess);
        Assert.NotNull(Assert.Single(await world.EpisodesAsync()).LiftedAt);
    }

    /// <summary>Any other agent failure leaves the episode in force.</summary>
    [Fact]
    public async Task Any_other_agent_failure_leaves_the_episode_in_force()
    {
        // Marking the row lifted while the kernel still drops the packets would tell an operator
        // the address is back while it is not.
        var world = new World(InForce("203.0.113.7"));
        world.Agent.UnbanResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.UnbanAsync("203.0.113.7");

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Null(Assert.Single(await world.EpisodesAsync()).LiftedAt);
    }

    /// <summary>A lift is journalled with the address as its subject.</summary>
    [Fact]
    public async Task A_lift_is_journalled_with_the_address_as_its_subject()
    {
        var world = new World(InForce("203.0.113.7"));

        await world.UnbanAsync("203.0.113.7");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AddressUnbanned, entry.Action);
        Assert.Equal("203.0.113.7", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused lift is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_lift_is_journalled_as_a_failure()
    {
        var world = new World();

        await world.UnbanAsync("203.0.113.7");

        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>Builds an episode that is still in force at <c>Now</c>.</summary>
    /// <param name="address">The banned address.</param>
    private static BanEpisode InForce(string address)
    {
        return new BanEpisode(
            Guid.NewGuid(),
            address,
            BanReason.BruteForce,
            Now.AddHours(-1),
            25,
            Now.AddHours(-1),
            Now.AddHours(1));
    }

    /// <summary>Builds an episode that has already run out.</summary>
    /// <param name="address">The banned address.</param>
    private static BanEpisode Expired(string address)
    {
        return new BanEpisode(
            Guid.NewGuid(),
            address,
            BanReason.BruteForce,
            Now.AddDays(-2),
            25,
            Now.AddDays(-2),
            Now.AddDays(-1));
    }

    /// <summary>The store, the agent double, the journal and the handler under test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database this world's contexts share.</summary>
        private readonly string _store = Guid.NewGuid().ToString();

        /// <summary>The agent double, which records what the panel decided to send.</summary>
        public RecordingAgentFirewallClient Agent { get; } = new();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Seeds the store with the episodes a test starts from.</summary>
        /// <param name="episodes">The rows the panel remembers.</param>
        public World(params BanEpisode[] episodes)
        {
            using var context = FirewallTestContext.Create(_store);
            context.BanEpisodes.AddRange(episodes);
            context.SaveChanges();
        }

        /// <summary>Runs the handler once, on its own context, the way a request does.</summary>
        /// <param name="address">The address the caller asked to release.</param>
        public async Task<Result<bool>> UnbanAsync(string address)
        {
            using var context = FirewallTestContext.Create(_store);
            var handler = new UnbanAddressCommandHandler(
                context,
                Agent,
                new FakeClock(Now),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()));

            return await handler.HandleAsync(
                new UnbanAddressCommand(address, "198.51.100.1", "curl"), CancellationToken.None);
        }

        /// <summary>Reads every episode the store holds.</summary>
        public async Task<List<BanEpisode>> EpisodesAsync()
        {
            using var context = FirewallTestContext.Create(_store);
            return await context.BanEpisodes.AsNoTracking().ToListAsync();
        }
    }
}
