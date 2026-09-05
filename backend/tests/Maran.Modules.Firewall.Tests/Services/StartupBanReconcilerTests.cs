using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Firewall.Tests.Services;

/// <summary>
/// What happens to the panel's bans when the machine comes back up. Both families' nftables units
/// flush on stop and the agent keeps no ban state, so this pass is the only reason a ban outlives a
/// restart — and the only reason an expired one does not.
/// </summary>
public sealed class StartupBanReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A ban that is still in force is re applied after a restart.</summary>
    [Fact]
    public async Task A_ban_that_is_still_in_force_is_re_applied_after_a_restart()
    {
        using var world = World(Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal("203.0.113.7", Assert.Single(world.Agent.Bans).Address);
    }

    /// <summary>The remaining time is re applied and never the original duration.</summary>
    [Fact]
    public async Task The_remaining_time_is_re_applied_and_never_the_original_duration()
    {
        // A panel restarted twenty-three hours into a twenty-four-hour ban that asked for
        // twenty-four hours again would hold the address for forty-seven — and a machine that
        // restarts often enough would hold it forever, with nothing in the journal saying so.
        using var world = World(Timed("203.0.113.7", Now.AddHours(-23), Now.AddHours(1)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>An expired episode is not re banned.</summary>
    [Fact]
    public async Task An_expired_episode_is_not_re_banned()
    {
        // Re-applying it would resurrect a ban the clock had already ended: an address the panel
        // considers free, blocked again by a restart nobody connects to it.
        using var world = World(Timed("203.0.113.7", Now.AddDays(-2), Now.AddDays(-1)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>An episode with less than a whole second left is not re banned.</summary>
    [Fact]
    public async Task An_episode_with_less_than_a_whole_second_left_is_not_re_banned()
    {
        // The wire carries whole seconds and 0 there means PERMANENT, so re-applying such a
        // remainder would install a ban nobody can wait out.
        using var world = World(Timed("203.0.113.7", Now.AddHours(-1), Now.AddMilliseconds(500)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A lifted episode is not re banned.</summary>
    [Fact]
    public async Task A_lifted_episode_is_not_re_banned()
    {
        var lifted = Permanent("203.0.113.7", Now.AddHours(-1));
        lifted.Lift(Now.AddMinutes(-5));
        using var world = World(lifted);

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A permanent ban is re applied with no expiry.</summary>
    [Fact]
    public async Task A_permanent_ban_is_re_applied_with_no_expiry()
    {
        // Null and not a duration: on the wire a duration of zero is how permanent is spelled, and
        // a computed remainder would eventually become one.
        using var world = World(Permanent("203.0.113.7", Now.AddYears(-1)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Null(Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>A pass the agent refuses reports failure so it can be retried.</summary>
    [Fact]
    public async Task A_pass_the_agent_refuses_reports_failure_so_it_can_be_retried()
    {
        // The agent and the panel are separate units with no ordering guarantee across a reboot, so
        // the first pass finding nothing listening is expected rather than exceptional.
        using var world = World(Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)));
        world.Agent.BanResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        Assert.False(await world.Reconciler.ReconcileAsync(CancellationToken.None));
    }

    /// <summary>One refused address does not stop the rest of the pass.</summary>
    [Fact]
    public async Task One_refused_address_does_not_stop_the_rest_of_the_pass()
    {
        // Stopping at the first refusal would leave the whole server unprotected because of one bad
        // row.
        using var world = World(
            Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)),
            Timed("198.51.100.9", Now.AddHours(-1), Now.AddHours(1)));
        world.Agent.AddressesThatFailToBan.Add("203.0.113.7");

        Assert.False(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(2, world.Agent.Bans.Count);
        Assert.Contains(world.Agent.Bans, ban =>
        {
            return ban.Address == "198.51.100.9";
        });
    }

    /// <summary>A pass with nothing in force succeeds without calling the agent.</summary>
    [Fact]
    public async Task A_pass_with_nothing_in_force_succeeds_without_calling_the_agent()
    {
        using var world = World();

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>A whitelisted address is not re banned after a restart.</summary>
    [Fact]
    public async Task A_whitelisted_address_is_not_re_banned_after_a_restart()
    {
        // R8 says the whitelist is checked before every automatic ban, and restoring one is still
        // placing one. This pass used to read only BanEpisodes, so an operator who whitelisted
        // themselves stayed exempt exactly until the panel restarted: the list held while the
        // process ran and was ignored the moment it came back. Deleting the guard in ReconcileAsync
        // turns this red.
        using var world = WorldWithWhitelist(
            [Office("203.0.113.0/24")],
            Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        // Not merely "fewer bans": none at all, and the pass still reports success. An exempt
        // episode counted as a failure would retry five times and then report a broken host.
        Assert.Empty(world.Agent.Bans);
    }

    /// <summary>An episode outside every whitelist range is still re applied.</summary>
    [Fact]
    public async Task An_episode_outside_every_whitelist_range_is_still_re_applied()
    {
        // The other half of the guard: a whitelist that exempted everyone would pass the test above
        // while silently disarming the reconciler, and nothing else here would notice.
        using var world = WorldWithWhitelist(
            [Office("198.51.100.0/24")],
            Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal("203.0.113.7", Assert.Single(world.Agent.Bans).Address);
    }

    /// <summary>An episode skipped because it is whitelisted is journalled like any other decision.</summary>
    [Fact]
    public async Task An_episode_skipped_because_it_is_whitelisted_is_journalled_like_any_other_decision()
    {
        // The detector's handler writes this entry for the identical decision. Written from here too,
        // the journal answers "why is this address not banned any more" instead of showing two
        // whitelist edits and no ban activity at all.
        using var world = WorldWithWhitelist(
            [Office("203.0.113.0/24")],
            Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BanSkippedWhitelisted, entry.Action);
        Assert.Equal("203.0.113.7", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A skipped episode is ended, so deleting the whitelist row cannot resurrect it.</summary>
    [Fact]
    public async Task A_skipped_episode_is_ended_so_deleting_the_whitelist_row_cannot_resurrect_it()
    {
        // 09:00 an address is auto-banned for a day. 10:00 an administrator whitelists its network.
        // 11:00 a reboot correctly does not restore the ban — and the panel used to go on listing it
        // as banned, so at 14:00 the operator tidies the whitelist row away and the 15:00 reboot
        // re-applied eighteen hours of a ban that had not been in effect for four.
        using var world = WorldWithWhitelist(
            [Office("203.0.113.0/24")],
            Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(23)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);
        await world.RemoveWhitelistAsync();
        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Empty(world.Agent.Bans);
        Assert.Equal(Now, Assert.Single(await world.EpisodesAsync()).LiftedAt);
    }

    /// <summary>A ban an administrator placed by hand is restored even on a whitelisted address.</summary>
    [Fact]
    public async Task A_ban_an_administrator_placed_by_hand_is_restored_even_on_a_whitelisted_address()
    {
        // The whitelist exempts an address from the DETECTOR, not from an administrator who decided
        // to block it — WhitelistEntry says so in as many words, and BanAddressCommandHandler acts
        // on it. Skipping a manual ban here would let a reboot undo a person's decision, and because
        // a skipped episode is lifted it would undo it permanently.
        using var world = WorldWithWhitelist(
            [Office("203.0.113.0/24")],
            Manual("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal("203.0.113.7", Assert.Single(world.Agent.Bans).Address);
        Assert.Empty(world.Audit.Entries);
    }

    /// <summary>The whitelist is read once for the whole pass rather than once per episode.</summary>
    [Fact]
    public async Task The_whitelist_is_read_once_for_the_whole_pass_rather_than_once_per_episode()
    {
        // A botnet wave leaves thousands of episodes in force, and the pass used to issue one full
        // read of the whitelist per episode — thousands of sequential round trips, every one of them
        // paid inside the window in which every banned address is reaching the host.
        using var world = WorldWithWhitelist(
            [Office("203.0.113.0/24")],
            Timed("198.51.100.1", Now.AddHours(-1), Now.AddHours(1)),
            Timed("198.51.100.2", Now.AddHours(-1), Now.AddHours(1)),
            Timed("198.51.100.3", Now.AddHours(-1), Now.AddHours(1)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(3, world.Agent.Bans.Count);
        Assert.Equal(1, world.WhitelistReads);
    }

    /// <summary>The pass reports the episodes it skipped instead of subtracting them from the total.</summary>
    [Fact]
    public async Task The_pass_reports_the_episodes_it_skipped_instead_of_subtracting_them_from_the_total()
    {
        // One in-force episode that is skipped used to be logged as "Re-applied 0 of 0", which reads
        // exactly like a panel holding no bans at all — and it is the only line an operator gets
        // about this pass.
        using var world = WorldWithWhitelist(
            [Office("203.0.113.0/24")],
            Timed("203.0.113.7", Now.AddHours(-1), Now.AddHours(1)),
            Timed("198.51.100.9", Now.AddHours(-1), Now.AddHours(1)));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        var line = Assert.Single(world.Log.Messages);
        Assert.Equal(
            "Re-applied 1 of 2 firewall bans that outlived the last restart; 1 were ended instead "
            + "because the address is now whitelisted",
            line);
    }

    /// <summary>A permanent episode beats a shorter one on the same address.</summary>
    [Fact]
    public async Task A_permanent_episode_beats_a_shorter_one_on_the_same_address()
    {
        // The panel can hold two in-force episodes for one address on purpose — an operator's and a
        // detector's, keyed by its detection window and counted by the escalation ladder — while the
        // host holds ONE set element, whose timeout a second `add element` replaces. A pass that
        // re-applied each row installed them in list order and left whichever it read last, so a
        // reboot restored either the permanent ban or the fifteen-minute one, at random.
        using var world = World(
            Permanent("203.0.113.7", Now.AddHours(-2)),
            Timed("203.0.113.7", Now, Now.AddMinutes(15)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Null(Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>The longest of two timed episodes on one address is what is restored.</summary>
    [Fact]
    public async Task The_longest_of_two_timed_episodes_on_one_address_is_what_is_restored()
    {
        using var world = World(
            Manual("203.0.113.7", Now.AddHours(-2), Now.AddHours(12)),
            Timed("203.0.113.7", Now, Now.AddMinutes(15)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(TimeSpan.FromHours(12), Assert.Single(world.Agent.Bans).Ttl);
    }

    /// <summary>Two addresses are still re applied separately.</summary>
    [Fact]
    public async Task Two_addresses_are_still_re_applied_separately()
    {
        // The control on the axis the two tests above can send blind: an implementation that
        // collapsed every in-force episode into one call would satisfy both of them and restore a
        // single ban on a host that owed two.
        using var world = World(
            Timed("203.0.113.7", Now, Now.AddMinutes(15)),
            Timed("198.51.100.9", Now, Now.AddHours(3)));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(2, world.Agent.Bans.Count);
        Assert.Contains(world.Agent.Bans, ban =>
        {
            return ban.Address == "203.0.113.7" && ban.Ttl == TimeSpan.FromMinutes(15);
        });
        Assert.Contains(world.Agent.Bans, ban =>
        {
            return ban.Address == "198.51.100.9" && ban.Ttl == TimeSpan.FromHours(3);
        });
    }

    /// <summary>Builds a timed brute force episode.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the ban was placed.</param>
    /// <param name="expiresAt">When it runs out.</param>
    private static BanEpisode Timed(string address, DateTimeOffset bannedAt, DateTimeOffset expiresAt)
    {
        return new BanEpisode(
            Guid.NewGuid(), address, BanReason.BruteForce, bannedAt, 25, bannedAt, expiresAt);
    }

    /// <summary>Builds a timed episode an administrator asked for.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the ban was placed.</param>
    /// <param name="expiresAt">When it runs out.</param>
    private static BanEpisode Manual(string address, DateTimeOffset bannedAt, DateTimeOffset expiresAt)
    {
        return new BanEpisode(Guid.NewGuid(), address, BanReason.Manual, null, 0, bannedAt, expiresAt);
    }

    /// <summary>Builds an episode that lasts until somebody lifts it.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When the ban was placed.</param>
    private static BanEpisode Permanent(string address, DateTimeOffset bannedAt)
    {
        return new BanEpisode(Guid.NewGuid(), address, BanReason.Manual, null, 0, bannedAt, null);
    }

    /// <summary>Builds a whitelist row.</summary>
    /// <param name="cidr">The exempt range.</param>
    private static WhitelistEntry Office(string cidr)
    {
        return new WhitelistEntry(Guid.NewGuid(), cidr, "the operator's office", Now);
    }

    /// <summary>Builds a reconciler over a store holding <paramref name="episodes"/>.</summary>
    /// <param name="episodes">The rows the panel remembers.</param>
    private static ReconcilerWorld World(params BanEpisode[] episodes)
    {
        return new ReconcilerWorld(episodes, []);
    }

    /// <summary>The store, seeded with both episodes and whitelist rows.</summary>
    /// <param name="whitelist">The exemptions the operator has recorded.</param>
    /// <param name="episodes">The rows the panel remembers.</param>
    /// <returns>A world whose reconciler reads both.</returns>
    private static ReconcilerWorld WorldWithWhitelist(
        IReadOnlyList<WhitelistEntry> whitelist, params BanEpisode[] episodes)
    {
        return new ReconcilerWorld(episodes, whitelist);
    }

    /// <summary>The store, the agent double, the journal double and the reconciler under test.</summary>
    private sealed class ReconcilerWorld : IDisposable
    {
        /// <summary>The context every scope of the reconciler resolves.</summary>
        private readonly FirewallDbContext _dbContext;

        /// <summary>The container the reconciler's scopes come from.</summary>
        private readonly TestScopeFactory _scopes;

        /// <summary>Counts the whitelist rows the store hands out.</summary>
        private readonly CountingWhitelistReads _reads = new();

        /// <summary>Keeps the lines the pass reports, which are all an operator sees of it.</summary>
        public RecordingLogger<StartupBanReconciler> Log { get; } = new();

        /// <summary>The agent double, which records what the panel decided to send.</summary>
        public RecordingAgentFirewallClient Agent { get; } = new();

        /// <summary>The journal double, which records what the panel decided not to send.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The reconciler under test.</summary>
        public StartupBanReconciler Reconciler { get; }

        /// <summary>How many whitelist rows the store has materialised since it was built.</summary>
        /// <remarks>
        /// With one whitelist row this is the number of times the table was read, which is the whole
        /// finding: a per-episode read gives the right ANSWER, and gets there by way of one round
        /// trip per banned address inside the window in which every one of them is reaching the host.
        /// </remarks>
        public int WhitelistReads
        {
            get
            {
                return _reads.Count;
            }
        }

        /// <summary>Seeds the store and builds the reconciler over it.</summary>
        /// <param name="episodes">The rows the panel remembers.</param>
        /// <param name="whitelist">The exemptions the operator has recorded.</param>
        public ReconcilerWorld(IReadOnlyList<BanEpisode> episodes, IReadOnlyList<WhitelistEntry> whitelist)
        {
            _dbContext = FirewallTestContext.Create(interceptor: _reads);
            _dbContext.BanEpisodes.AddRange(episodes);
            _dbContext.WhitelistEntries.AddRange(whitelist);
            _dbContext.SaveChanges();

            _scopes = new TestScopeFactory(_dbContext, Agent, Audit);
            Reconciler = new StartupBanReconciler(_scopes.Scopes, new FakeClock(Now), Log);
        }

        /// <summary>Deletes every whitelist row, the way an operator tidying up does.</summary>
        public async Task RemoveWhitelistAsync()
        {
            _dbContext.WhitelistEntries.RemoveRange(await _dbContext.WhitelistEntries.ToListAsync());
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>Reads every episode the store holds.</summary>
        public async Task<List<BanEpisode>> EpisodesAsync()
        {
            return await _dbContext.BanEpisodes.AsNoTracking().ToListAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Reconciler.Dispose();
            _scopes.Dispose();
            _dbContext.Dispose();
        }
    }
}
