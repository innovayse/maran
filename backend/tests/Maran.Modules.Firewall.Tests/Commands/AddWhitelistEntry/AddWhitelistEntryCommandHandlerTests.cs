using Maran.Modules.Firewall.Commands.AddWhitelistEntry;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Firewall.Tests.Commands.AddWhitelistEntry;

/// <summary>Adding an exemption from the panel's automatic bans.</summary>
public sealed class AddWhitelistEntryCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Adding a range stores it and journals the change.</summary>
    [Fact]
    public async Task Adding_a_range_stores_it_and_journals_the_change()
    {
        var world = new World();

        var result = await world.AddAsync("203.0.113.7/32", "office");

        Assert.True(result.IsSuccess);
        Assert.Equal("203.0.113.7/32", result.Value.Cidr);
        Assert.Equal(Now, result.Value.CreatedAt);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.FirewallWhitelistChanged, entry.Action);
        Assert.Equal("203.0.113.7/32", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Adding a range that is already exempt answers conflict and stores nothing.</summary>
    [Fact]
    public async Task Adding_a_range_that_is_already_exempt_answers_conflict_and_stores_nothing()
    {
        // Two rows for one range are not merely untidy: removing one would leave the exemption in
        // place while the screen said it had gone, which an operator would discover by being banned.
        var world = new World();
        await world.AddAsync("203.0.113.7/32", "office");

        var result = await world.AddAsync("203.0.113.7/32", "office again");

        Assert.False(result.IsSuccess);
        Assert.Equal("WhitelistCidrTaken", result.Error!.Code);
        Assert.Single(await world.EntriesAsync());
    }

    /// <summary>A refused addition is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_addition_is_journalled_as_a_failure()
    {
        var world = new World();
        await world.AddAsync("203.0.113.7/32", "office");

        await world.AddAsync("203.0.113.7/32", "office again");

        Assert.False(world.Audit.Entries[^1].Succeeded);
    }

    /// <summary>The whitelist never reaches the agent.</summary>
    [Fact]
    public async Task The_whitelist_never_reaches_the_agent()
    {
        // A whitelist row is an exemption from the PANEL's brute-force banning, not a hole in the
        // host's packet filter. Sending it to the agent would turn "do not ban this by accident"
        // into "always let this in", which is a far larger promise than anybody made.
        var world = new World();

        await world.AddAsync("203.0.113.7/32", "office");

        Assert.Empty(world.Agent.Allows);
        Assert.Empty(world.Agent.Bans);
        Assert.Empty(world.Agent.Unbans);
    }

    /// <summary>The store, the journal and the handler under test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database this world's contexts share.</summary>
        private readonly string _store = Guid.NewGuid().ToString();

        /// <summary>An agent double that must stay untouched.</summary>
        public RecordingAgentFirewallClient Agent { get; } = new();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Runs the handler once, on its own context, the way a request does.</summary>
        /// <param name="cidr">The range to exempt.</param>
        /// <param name="note">What the range is.</param>
        public async Task<Result<WhitelistEntryDto>> AddAsync(string cidr, string note)
        {
            using var context = FirewallTestContext.Create(_store);
            var handler = new AddWhitelistEntryCommandHandler(
                context,
                new FakeClock(Now),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()));

            return await handler.HandleAsync(
                new AddWhitelistEntryCommand(cidr, note, "198.51.100.1", "curl"), CancellationToken.None);
        }

        /// <summary>Reads every whitelist row the store holds.</summary>
        public async Task<List<WhitelistEntry>> EntriesAsync()
        {
            using var context = FirewallTestContext.Create(_store);
            return await context.WhitelistEntries.AsNoTracking().ToListAsync();
        }
    }
}
