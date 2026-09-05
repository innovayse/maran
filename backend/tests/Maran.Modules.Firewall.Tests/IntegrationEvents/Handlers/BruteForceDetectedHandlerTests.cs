using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.IntegrationEvents.Handlers;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Firewall.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What the panel does with a brute-force detection: who it bans, for how long, who it never bans,
/// and what it writes down.
/// </summary>
public sealed class BruteForceDetectedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A whitelisted address is not banned.</summary>
    [Fact]
    public async Task A_whitelisted_address_is_not_banned()
    {
        // The one control between this panel and banning its own operator. The detector cannot tell
        // an administrator mistyping a password from an attack; this row is the difference.
        var world = await WorldAsync(whitelisted: "203.0.113.7/32");

        await world.HandleAsync("203.0.113.7");

        Assert.Empty(world.Agent.Bans);
        Assert.Empty(await world.EpisodesAsync());
    }

    /// <summary>A whitelisted network exempts every address inside it.</summary>
    [Fact]
    public async Task A_whitelisted_network_exempts_every_address_inside_it()
    {
        var world = await WorldAsync(whitelisted: "203.0.113.0/24");

        await world.HandleAsync("203.0.113.200");

        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A skipped ban is journalled as skipped and never as a failure.</summary>
    [Fact]
    public async Task A_skipped_ban_is_journalled_as_skipped_and_never_as_a_failure()
    {
        // Nothing went wrong, and the absence of a ban an operator expected is exactly what this
        // entry explains. Without it, a whitelist that had quietly grown too wide looks like a
        // detector that has stopped working.
        var world = await WorldAsync(whitelisted: "203.0.113.7/32");

        await world.HandleAsync("203.0.113.7");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BanSkippedWhitelisted, entry.Action);
        Assert.Equal("203.0.113.7", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An address outside the whitelist is banned.</summary>
    [Fact]
    public async Task An_address_outside_the_whitelist_is_banned()
    {
        // Guards every skip test above from passing for the wrong reason: a handler that banned
        // nobody at all would satisfy them.
        var world = await WorldAsync(whitelisted: "198.51.100.0/24");

        await world.HandleAsync("203.0.113.7");

        var call = Assert.Single(world.Agent.Bans);
        Assert.Equal("203.0.113.7", call.Address);
    }

    /// <summary>A first detection bans for fifteen minutes.</summary>
    [Fact]
    public async Task A_first_detection_bans_for_fifteen_minutes()
    {
        var world = await WorldAsync();

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromMinutes(15), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>A second detection inside the day bans for an hour.</summary>
    [Fact]
    public async Task A_second_detection_inside_the_day_bans_for_an_hour()
    {
        // Escalation is what separates the person who mistyped a password from the script that
        // waits out a fixed ban and resumes.
        var world = await WorldAsync();
        await world.HandleAsync("203.0.113.7", windowStart: Now.AddMinutes(-30));

        await world.HandleAsync("203.0.113.7", windowStart: Now);

        Assert.Equal([TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)], world.Agent.Bans.Select(ban =>
        {
            return ban.Ttl;
        }));
    }

    /// <summary>A third detection inside the day bans for a day.</summary>
    [Fact]
    public async Task A_third_detection_inside_the_day_bans_for_a_day()
    {
        var world = await WorldAsync();
        await world.HandleAsync("203.0.113.7", windowStart: Now.AddMinutes(-60));
        await world.HandleAsync("203.0.113.7", windowStart: Now.AddMinutes(-30));

        await world.HandleAsync("203.0.113.7", windowStart: Now);

        Assert.Equal(TimeSpan.FromHours(24), world.Agent.Bans[^1].Ttl);
    }

    /// <summary>An episode an administrator lifted still counts on the ladder.</summary>
    [Fact]
    public async Task An_episode_an_administrator_lifted_still_counts_on_the_ladder()
    {
        // Unbanning says "let this address back in now", not "this never happened". Counting only
        // the episodes still in force would let an address be unbanned and re-offend indefinitely
        // at fifteen minutes a time, never reaching the hour rung, let alone the day one — and the
        // tidy-up edit that causes it (`&& episode.LiftedAt == null` on the count below) used to
        // leave the whole suite green.
        var world = await WorldAsync();
        var lifted = Episode("203.0.113.7", Now.AddHours(-2));
        lifted.Lift(Now.AddHours(-1));
        world.Seed(lifted);

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>An episode older than the day does not escalate the next ban.</summary>
    [Fact]
    public async Task An_episode_older_than_the_day_does_not_escalate_the_next_ban()
    {
        // Longer than a day and somebody who mistyped a password on Monday is still paying for it
        // on Friday.
        var world = await WorldAsync();
        world.Seed(Episode("203.0.113.7", Now.AddDays(-2)));

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromMinutes(15), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>A ban reaches the agent as plain ipv4 when the detector reported a mapped address.</summary>
    [Fact]
    public async Task A_ban_reaches_the_agent_as_plain_ipv4_when_the_detector_reported_a_mapped_address()
    {
        // A dual-stack listener reports an IPv4 peer as ::ffff:a.b.c.d, and the agent refuses that
        // form deliberately: a mapped address in the IPv6 ban set matches no IPv4 packet. Without
        // this normalisation every brute-force ban on a dual-stack host is rejected and the whole
        // feature is inert, while every other test in this file stays green.
        var world = await WorldAsync();

        await world.HandleAsync("::ffff:203.0.113.7");

        Assert.Equal("203.0.113.7", Assert.Single(world.Agent.Bans).Address);
        Assert.Equal("203.0.113.7", Assert.Single(await world.EpisodesAsync()).IpAddress);
    }

    /// <summary>A mapped address is matched against the whitelist in its plain form.</summary>
    [Fact]
    public async Task A_mapped_address_is_matched_against_the_whitelist_in_its_plain_form()
    {
        // The other half of the same bug: an operator whitelists 203.0.113.7/32, the listener
        // reports ::ffff:203.0.113.7, and an unnormalised comparison exempts nobody.
        var world = await WorldAsync(whitelisted: "203.0.113.7/32");

        await world.HandleAsync("::ffff:203.0.113.7");

        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A redelivered detection bans nothing a second time.</summary>
    [Fact]
    public async Task A_redelivered_detection_bans_nothing_a_second_time()
    {
        // A durable queue may deliver the same detection twice; that is the queue behaving
        // correctly. A second delivery must extend nothing and count as no second offence, or a
        // redelivery storm escalates an address to a day on its first mistake.
        var world = await WorldAsync();
        await world.HandleAsync("203.0.113.7", windowStart: Now);

        await world.HandleAsync("203.0.113.7", windowStart: Now);

        Assert.Single(world.Agent.Bans);
        Assert.Single(await world.EpisodesAsync());
    }

    /// <summary>A delivery whose episode the database refuses is not thrown out of the handler.</summary>
    [Fact]
    public async Task A_delivery_whose_episode_the_database_refuses_is_not_thrown_out_of_the_handler()
    {
        // Two simultaneous deliveries of one detection both pass the redelivery read, and the unique
        // index on (IpAddress, WindowStart) refuses the second — the index doing its job. Letting
        // that escape breaks this class's one promise: an exception returns the message to the
        // queue, and the retry climbs the escalation ladder in place of the attacker. The in-memory
        // provider enforces no unique index, so the refusal is staged.
        var world = await WorldAsync();

        await world.HandleAsync("203.0.113.7", interceptor: new RefusingSaveChanges());

        // The ban did reach the host; only the panel's record of it was refused, so nothing is
        // journalled as a ban that succeeded and nothing is journalled as a failure either.
        Assert.Single(world.Agent.Bans);
        Assert.Empty(world.Audit.Entries);
    }

    /// <summary>A ban the agent refuses is journalled as a failure and leaves no row.</summary>
    [Fact]
    public async Task A_ban_the_agent_refuses_is_journalled_as_a_failure_and_leaves_no_row()
    {
        // A row for a ban that never happened would make the panel report an address as banned
        // while every packet from it arrives — and the reconciler would go on re-applying a ban
        // that has never once existed.
        var world = await WorldAsync();
        world.Agent.BanResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        await world.HandleAsync("203.0.113.7");

        Assert.Empty(await world.EpisodesAsync());
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AddressBanned, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A ban is journalled under the panels own actor name and not a users.</summary>
    [Fact]
    public async Task A_ban_is_journalled_under_the_panels_own_actor_name_and_not_a_users()
    {
        // Nobody signed in placed this ban. An automatic ban recorded as a person's action would
        // be a false accusation in an append-only journal.
        var world = await WorldAsync();

        await world.HandleAsync("203.0.113.7");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.AddressBanned, entry.Action);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(FirewallAuditJournal.SystemActor, entry.ActorUsername);
        Assert.Equal("203.0.113.7", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An episode records the reason and the count the agent could never hold.</summary>
    [Fact]
    public async Task An_episode_records_the_reason_and_the_count_the_agent_could_never_hold()
    {
        var world = await WorldAsync();

        await world.HandleAsync("203.0.113.7", failures: 25);

        var episode = Assert.Single(await world.EpisodesAsync());
        Assert.Equal(BanReason.BruteForce, episode.Reason);
        Assert.Equal(25, episode.Failures);
        Assert.Equal(Now.AddMinutes(15), episode.ExpiresAt);
    }

    /// <summary>An automatic ban never shortens a standing permanent one.</summary>
    [Fact]
    public async Task An_automatic_ban_never_shortens_a_standing_permanent_one()
    {
        // The host's ban set is keyed by address and a second `add element` REPLACES the element
        // and its timeout, so a fifteen-minute automatic ban placed on top of an operator's
        // permanent one used to hand the kernel a fifteen-minute element — and the attacker whose
        // knocking triggered the automatic ban is the one who set that in motion.
        var world = await WorldAsync();
        world.Seed(PermanentManualEpisode("203.0.113.7", Now.AddHours(-2)));

        await world.HandleAsync("203.0.113.7");

        Assert.Null(Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>A standing permanent ban is still permanent in the panel afterwards.</summary>
    [Fact]
    public async Task A_standing_permanent_ban_is_still_permanent_in_the_panel_afterwards()
    {
        // Both halves of the record, because these rows are the only evidence of a ban that exists
        // anywhere and the reconciler re-applies whichever of them it reads last: the operator's
        // row still says permanent, and the automatic episode written beside it says the same
        // rather than contradicting it.
        var world = await WorldAsync();
        world.Seed(PermanentManualEpisode("203.0.113.7", Now.AddHours(-2)));

        await world.HandleAsync("203.0.113.7");

        Assert.All(await world.EpisodesAsync(), episode =>
        {
            Assert.Null(episode.ExpiresAt);
        });
    }

    /// <summary>An automatic ban never shortens a standing timed ban that runs longer.</summary>
    [Fact]
    public async Task An_automatic_ban_never_shortens_a_standing_timed_ban_that_runs_longer()
    {
        // The same defect one rung down: the ladder's hour would replace twelve hours an operator
        // asked for by hand.
        var world = await WorldAsync();
        world.Seed(ManualEpisode("203.0.113.7", Now.AddHours(-2), Now.AddHours(12)));

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromHours(12), Assert.Single(world.Agent.Bans).Ttl);
        Assert.All(await world.EpisodesAsync(), episode =>
        {
            Assert.Equal(Now.AddHours(12), episode.ExpiresAt);
        });
    }

    /// <summary>An automatic ban that runs longer than the standing one still extends it.</summary>
    [Fact]
    public async Task An_automatic_ban_that_runs_longer_than_the_standing_one_still_extends_it()
    {
        // The inverse of the rule above, and the cost of getting it wrong in the other direction:
        // an address banned briefly by hand and then genuinely attacking must still be held for the
        // ladder's rung. Both rows then carry the ladder's expiry, because the kernel holds one
        // element and cannot carry two.
        var world = await WorldAsync();
        world.Seed(ManualEpisode("203.0.113.7", Now.AddHours(-2), Now.AddMinutes(5)));

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(world.Agent.Bans).Ttl);

        // The operator's row is NOT moved to match. The host holds one element, the panel holds two
        // rows describing it, and the ban in force is the longest of them — which is the rule the
        // reconciler restores. Editing the operator's row here would be this handler writing a row
        // another decision owns, and that is the concurrency hazard the test below is about.
        var episodes = await world.EpisodesAsync();
        Assert.Equal(Now.AddMinutes(5), episodes[0].ExpiresAt);
        Assert.Equal(Now.AddHours(1), episodes[1].ExpiresAt);
    }

    /// <summary>An automatic ban edits no episode but the one it adds.</summary>
    [Fact]
    public async Task An_automatic_ban_edits_no_episode_but_the_one_it_adds()
    {
        // The property that makes the concurrent case safe, asserted directly rather than inferred.
        // Both this handler and the manual one write firewall.BanEpisodes for the same address with
        // no transaction, no row lock and no concurrency token, so if an automatic ban could
        // reschedule a standing row the outcome would be decided by whichever SaveChangesAsync
        // committed last — and the defect this whole file is about would return through that door.
        var world = await WorldAsync();
        var manual = ManualEpisode("203.0.113.7", Now.AddHours(-2), Now.AddMinutes(5));
        world.Seed(manual);

        await world.HandleAsync("203.0.113.7");

        var stored = (await world.EpisodesAsync()).Single(episode =>
        {
            return episode.Id == manual.Id;
        });
        Assert.Equal(Now.AddMinutes(5), stored.ExpiresAt);
        Assert.Equal(Now.AddHours(-2), stored.BannedAt);
        Assert.Null(stored.LiftedAt);
        Assert.Equal(BanReason.Manual, stored.Reason);
    }

    /// <summary>An administrators permanent ban wins whichever handler committed last.</summary>
    [Fact]
    public async Task An_administrators_permanent_ban_wins_whichever_handler_committed_last()
    {
        // The concurrent case, staged as the two commit orders it can resolve to. A detection is
        // handled against an address with nothing standing — the read a concurrent manual ban has
        // not yet landed in front of — and the administrator's permanent episode arrives afterwards.
        // The panel's answer for the address is the LONGEST of its in-force episodes, so it is
        // permanent either way round: the operator's decision wins as a property of the data, not of
        // timing. The opposite order is the sequential case the tests above cover.
        var world = await WorldAsync();

        await world.HandleAsync("203.0.113.7");
        world.Seed(PermanentManualEpisode("203.0.113.7", Now));

        var episodes = await world.EpisodesAsync();
        Assert.Equal(2, episodes.Count);
        Assert.Contains(episodes, episode =>
        {
            return episode.ExpiresAt is null;
        });
    }

    /// <summary>A permanent ban an administrator lifted does not lengthen the next automatic one.</summary>
    [Fact]
    public async Task A_permanent_ban_an_administrator_lifted_does_not_lengthen_the_next_automatic_one()
    {
        // The gate's control on the axis that can go blind. An implementation that read every
        // unexpired-looking row instead of every row IN FORCE would make one lifted permanent ban
        // pin the address to a permanent ban for the rest of the day, and every test above would
        // stay green. Lifting says "let this address back in now"; it still counts as a rung.
        var world = await WorldAsync();
        var lifted = PermanentManualEpisode("203.0.113.7", Now.AddHours(-2));
        lifted.Lift(Now.AddHours(-1));
        world.Seed(lifted);

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>An episode that has run out does not lengthen the next automatic ban.</summary>
    [Fact]
    public async Task An_episode_that_has_run_out_does_not_lengthen_the_next_automatic_ban()
    {
        // The other half of the same control: a twelve-hour ban that ended an hour ago is history
        // the ladder reads, not a ban the kernel is holding.
        var world = await WorldAsync();
        world.Seed(ManualEpisode("203.0.113.7", Now.AddHours(-13), Now.AddHours(-1)));

        await world.HandleAsync("203.0.113.7");

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>A detection naming something that is not an address bans nothing.</summary>
    [Fact]
    public async Task A_detection_naming_something_that_is_not_an_address_bans_nothing()
    {
        var world = await WorldAsync();

        await world.HandleAsync("not-an-address");

        Assert.Empty(world.Agent.Bans);
        Assert.Empty(await world.EpisodesAsync());
    }

    /// <summary>Builds a world with an optional whitelist row.</summary>
    /// <param name="whitelisted">A range to exempt, or null for an empty whitelist.</param>
    private static async Task<World> WorldAsync(string? whitelisted = null)
    {
        var store = Guid.NewGuid().ToString();

        if (whitelisted is not null)
        {
            using var seed = FirewallTestContext.Create(store);
            seed.WhitelistEntries.Add(new WhitelistEntry(Guid.NewGuid(), whitelisted, "office", Now));
            await seed.SaveChangesAsync();
        }

        return new World(store);
    }

    /// <summary>Builds a past episode for the escalation window tests.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the ban was placed.</param>
    private static BanEpisode Episode(string address, DateTimeOffset bannedAt)
    {
        return new BanEpisode(
            Guid.NewGuid(), address, BanReason.BruteForce, bannedAt, 25, bannedAt, bannedAt.AddMinutes(15));
    }

    /// <summary>Builds a manual episode that lasts until somebody lifts it.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the administrator placed it.</param>
    private static BanEpisode PermanentManualEpisode(string address, DateTimeOffset bannedAt)
    {
        return new BanEpisode(
            Guid.NewGuid(), address, BanReason.Manual, windowStart: null, failures: 0, bannedAt, expiresAt: null);
    }

    /// <summary>Builds a manual episode that runs out at a stated instant.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the administrator placed it.</param>
    /// <param name="expiresAt">When it runs out.</param>
    private static BanEpisode ManualEpisode(string address, DateTimeOffset bannedAt, DateTimeOffset expiresAt)
    {
        return new BanEpisode(
            Guid.NewGuid(), address, BanReason.Manual, windowStart: null, failures: 0, bannedAt, expiresAt);
    }

    /// <summary>Everything one test needs: the store, the agent, the journal, and the handler.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database this world's contexts share.</summary>
        public string Store { get; }

        /// <summary>The agent double, which records what the panel decided to send.</summary>
        public RecordingAgentFirewallClient Agent { get; } = new();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Creates a world over <paramref name="store"/>.</summary>
        /// <param name="store">The in-memory database name.</param>
        public World(string store)
        {
            Store = store;
        }

        /// <summary>Adds a row straight to the store, bypassing the handler.</summary>
        /// <param name="episode">The episode to seed.</param>
        public void Seed(BanEpisode episode)
        {
            using var context = FirewallTestContext.Create(Store);
            context.BanEpisodes.Add(episode);
            context.SaveChanges();
        }

        /// <summary>Runs the handler once, on its own context, the way a message delivery does.</summary>
        /// <param name="address">The address the detector reported.</param>
        /// <param name="windowStart">The window the failures were counted over.</param>
        /// <param name="failures">How many failures were counted.</param>
        /// <param name="interceptor">Stands in for a database that refuses the write, when a test needs one.</param>
        public async Task HandleAsync(
            string address,
            DateTimeOffset? windowStart = null,
            int failures = 25,
            IInterceptor? interceptor = null)
        {
            using var context = FirewallTestContext.Create(Store, interceptor);
            var handler = new BruteForceDetectedHandler(
                context,
                new WhitelistGuard(context),
                Agent,
                new FakeClock(Now),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()),
                NullLogger<BruteForceDetectedHandler>.Instance);

            await handler.HandleAsync(
                new BruteForceDetected(address, failures, windowStart ?? Now), CancellationToken.None);
        }

        /// <summary>Reads every episode the store holds.</summary>
        public async Task<List<BanEpisode>> EpisodesAsync()
        {
            using var context = FirewallTestContext.Create(Store);
            return await context.BanEpisodes.AsNoTracking().OrderBy(episode => episode.BannedAt).ToListAsync();
        }
    }
}
