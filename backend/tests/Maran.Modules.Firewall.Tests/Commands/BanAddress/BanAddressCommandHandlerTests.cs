using Maran.Modules.Firewall.Commands.BanAddress;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Firewall.Tests.Commands.BanAddress;

/// <summary>What an administrator's own ban sends, records and journals.</summary>
public sealed class BanAddressCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A manual ban reaches the agent as plain ipv4 when the caller wrote a mapped address.</summary>
    [Fact]
    public async Task A_manual_ban_reaches_the_agent_as_plain_ipv4_when_the_caller_wrote_a_mapped_address()
    {
        // The agent refuses ::ffff:a.b.c.d deliberately — a mapped address in the IPv6 ban set
        // matches no IPv4 packet — so a ban sent in that form would be rejected, or worse accepted
        // and inert.
        var world = new World();

        var result = await world.BanAsync("::ffff:203.0.113.7", durationMinutes: 60);

        Assert.True(result.IsSuccess);
        Assert.Equal("203.0.113.7", Assert.Single(world.Agent.Bans).Address);
    }

    /// <summary>A manual ban records an episode so it survives a reboot.</summary>
    [Fact]
    public async Task A_manual_ban_records_an_episode_so_it_survives_a_reboot()
    {
        // Without the row an administrator's deliberate ban would be the one kind that quietly
        // stops being in force: the agent keeps no ban state and the nftables unit flushes on stop.
        var world = new World();

        await world.BanAsync("203.0.113.7", durationMinutes: 60);

        var episode = Assert.Single(await world.EpisodesAsync());
        Assert.Equal("203.0.113.7", episode.IpAddress);
        Assert.Equal(BanReason.Manual, episode.Reason);
        Assert.Null(episode.WindowStart);
        Assert.Equal(Now.AddHours(1), episode.ExpiresAt);
    }

    /// <summary>A ban with no duration is recorded and sent as permanent.</summary>
    [Fact]
    public async Task A_ban_with_no_duration_is_recorded_and_sent_as_permanent()
    {
        var world = new World();

        await world.BanAsync("203.0.113.7", durationMinutes: null);

        Assert.Null(Assert.Single(world.Agent.Bans).Ttl);
        Assert.Null(Assert.Single(await world.EpisodesAsync()).ExpiresAt);
    }

    /// <summary>A ban the agent refuses leaves no row.</summary>
    [Fact]
    public async Task A_ban_the_agent_refuses_leaves_no_row()
    {
        // A row for a ban that never happened would report an address as banned while every packet
        // from it arrives, and the reconciler would re-apply it forever.
        var world = new World();
        world.Agent.BanResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.BanAsync("203.0.113.7", durationMinutes: 60);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Empty(await world.EpisodesAsync());
    }

    /// <summary>A ban of something that is not an address is refused before the agent is called.</summary>
    [Fact]
    public async Task A_ban_of_something_that_is_not_an_address_is_refused_before_the_agent_is_called()
    {
        var world = new World();

        var result = await world.BanAsync("not-an-address", durationMinutes: 60);

        Assert.False(result.IsSuccess);
        Assert.Equal("BanAddressInvalid", result.Error!.Code);
        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A ban on a loopback address is refused with a code of its own, before the agent is called.</summary>
    [Fact]
    public async Task A_ban_on_a_loopback_address_is_refused_with_a_code_of_its_own()
    {
        // The agent refuses it too and always will; what this asks for is a refusal the operator can
        // act on. "Some of the details you submitted were not accepted" is the same nine words a
        // mismatched certificate key gets, and neither reader learns what to change.
        var world = new World();

        var result = await world.BanAsync("127.0.0.1", durationMinutes: 60);

        Assert.False(result.IsSuccess);
        Assert.Equal("BanAddressLoopback", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Empty(world.Agent.Bans);
        Assert.Empty(await world.EpisodesAsync());
    }

    /// <summary>A ban on the ipv6 loopback address is refused with the same code.</summary>
    [Fact]
    public async Task A_ban_on_the_ipv6_loopback_address_is_refused_with_the_same_code()
    {
        var world = new World();

        var result = await world.BanAsync("::1", durationMinutes: 60);

        Assert.False(result.IsSuccess);
        Assert.Equal("BanAddressLoopback", result.Error!.Code);
        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A ban on the ipv4-mapped loopback address is refused with the same code.</summary>
    [Fact]
    public async Task A_ban_on_the_mapped_loopback_address_is_refused_with_the_same_code()
    {
        // The composition of the two cases above, and the one neither of them covers: the panel's
        // check is IPAddress.IsLoopback, which answers FALSE for the mapped spelling as an IPv6
        // value. It only ever sees the unwrapped form because IpAddressNormalizer.TryNormalize runs
        // first — so this test pins the ORDER of those two steps, not either of them alone. Swap
        // them and the address reaches the agent, which refuses mapped notation with the generic
        // code, which is precisely the sentence the loopback code exists to stop showing.
        var world = new World();

        var result = await world.BanAsync("::ffff:127.0.0.1", durationMinutes: 60);

        Assert.False(result.IsSuccess);
        Assert.Equal("BanAddressLoopback", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Empty(world.Agent.Bans);
        Assert.Empty(await world.EpisodesAsync());
    }

    /// <summary>The inverse control: an address one step outside the loopback range is still banned.</summary>
    [Theory]
    [InlineData("126.255.255.255")]
    [InlineData("128.0.0.0")]
    [InlineData("203.0.113.7")]
    public async Task An_address_outside_the_loopback_range_is_still_banned(string address)
    {
        // A gate mutated to refuse everything passes every test that only hands it loopback.
        var world = new World();

        var result = await world.BanAsync(address, durationMinutes: 60);

        Assert.True(result.IsSuccess);
        Assert.Equal(address, Assert.Single(world.Agent.Bans).Address);
        Assert.Single(await world.EpisodesAsync());
    }

    /// <summary>Banning an address already banned moves the standing episode rather than adding a second.</summary>
    [Fact]
    public async Task Banning_an_address_already_banned_moves_the_standing_episode()
    {
        // An nftables set is keyed by address, so the second add REPLACES the timeout and the host
        // holds exactly one element. Two rows would then disagree about one kernel element, and the
        // older one's expiry would be a statement about this host that is not true.
        var world = new World();

        await world.BanAsync("203.0.113.7", durationMinutes: 9);
        var result = await world.BanAsync("203.0.113.7", durationMinutes: 30);

        Assert.True(result.IsSuccess);
        var episode = Assert.Single(await world.EpisodesAsync());
        Assert.Equal(Now.AddMinutes(30), episode.ExpiresAt);
        Assert.Equal(Now, episode.BannedAt);
        Assert.Null(episode.LiftedAt);
        Assert.Equal(2, world.Agent.Bans.Count);
    }

    /// <summary>Re-banning with no duration makes the standing episode permanent, as the kernel does.</summary>
    [Fact]
    public async Task Re_banning_with_no_duration_makes_the_standing_episode_permanent()
    {
        var world = new World();

        await world.BanAsync("203.0.113.7", durationMinutes: 9);
        await world.BanAsync("203.0.113.7", durationMinutes: null);

        Assert.Null(Assert.Single(await world.EpisodesAsync()).ExpiresAt);
    }

    /// <summary>The inverse control: a ban placed after the last one ran out is a second episode.</summary>
    [Fact]
    public async Task A_ban_placed_after_the_previous_one_ran_out_is_a_second_episode()
    {
        // The escalation ladder counts an address's episodes, so a ban that follows a finished one
        // must still be its own row — moving the old one would erase the history the ladder reads.
        var world = new World();
        world.Store(new BanEpisode(
            Guid.NewGuid(),
            "203.0.113.7",
            BanReason.Manual,
            windowStart: null,
            failures: 0,
            Now.AddHours(-2),
            Now.AddHours(-1)));

        await world.BanAsync("203.0.113.7", durationMinutes: 30);

        var episodes = await world.EpisodesAsync();
        Assert.Equal(2, episodes.Count);
        Assert.Single(episodes, episode => { return episode.ExpiresAt == Now.AddMinutes(30); });
    }

    /// <summary>A ban is journalled with the normalised address as its subject.</summary>
    [Fact]
    public async Task A_ban_is_journalled_with_the_normalised_address_as_its_subject()
    {
        var world = new World();

        await world.BanAsync("::ffff:203.0.113.7", durationMinutes: 60);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AddressBanned, entry.Action);
        Assert.Equal("203.0.113.7", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused ban is journalled as a failure naming what was attempted.</summary>
    [Fact]
    public async Task A_refused_ban_is_journalled_as_a_failure_naming_what_was_attempted()
    {
        var world = new World();
        world.Agent.BanResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        await world.BanAsync("203.0.113.7", durationMinutes: 60);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AddressBanned, entry.Action);
        Assert.Equal("203.0.113.7", entry.Subject);
        Assert.False(entry.Succeeded);
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

        /// <summary>Runs the handler once, on its own context, the way a request does.</summary>
        /// <param name="address">The address the caller asked to ban.</param>
        /// <param name="durationMinutes">How long for, or null for permanent.</param>
        public async Task<Result<bool>> BanAsync(string address, int? durationMinutes)
        {
            using var context = FirewallTestContext.Create(_store);
            var handler = new BanAddressCommandHandler(
                context,
                Agent,
                new FakeClock(Now),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()));

            return await handler.HandleAsync(
                new BanAddressCommand(address, durationMinutes, "198.51.100.1", "curl"),
                CancellationToken.None);
        }

        /// <summary>Puts an episode in the store, the way an earlier request would have.</summary>
        /// <param name="episode">The episode to store.</param>
        public void Store(BanEpisode episode)
        {
            using var context = FirewallTestContext.Create(_store);
            context.BanEpisodes.Add(episode);
            context.SaveChanges();
        }

        /// <summary>Reads every episode the store holds.</summary>
        public async Task<List<BanEpisode>> EpisodesAsync()
        {
            using var context = FirewallTestContext.Create(_store);
            return await context.BanEpisodes.AsNoTracking().ToListAsync();
        }
    }
}
