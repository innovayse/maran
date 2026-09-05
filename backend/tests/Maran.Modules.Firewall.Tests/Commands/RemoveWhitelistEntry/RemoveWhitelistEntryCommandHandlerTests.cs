using Maran.Modules.Firewall.Commands.RemoveWhitelistEntry;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Seeders;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Firewall.Tests.Commands.RemoveWhitelistEntry;

/// <summary>Removing an exemption, what the journal is left holding, and the one removal refused.</summary>
public sealed class RemoveWhitelistEntryCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The range the fixture exempts, and the address an ordinary caller arrives on.</summary>
    private const string Office = "203.0.113.7/32";

    /// <summary>An address outside <see cref="Office"/>; the caller most of these tests are.</summary>
    private const string Elsewhere = "198.51.100.1";

    /// <summary>The only address <see cref="Office"/> covers.</summary>
    private const string InsideOffice = "203.0.113.7";

    /// <summary>Removing an exemption journals the range and not the row id.</summary>
    [Fact]
    public async Task Removing_an_exemption_journals_the_range_and_not_the_row_id()
    {
        // After the row is gone the identifier means nothing to anybody. "Who stopped exempting the
        // office, and when" is the question this entry has to answer for an administrator who has
        // just been banned.
        var world = new World(out var entryId);

        var result = await world.RemoveAsync(entryId);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.FirewallWhitelistChanged, entry.Action);
        Assert.Equal(Office, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Removing an exemption takes the row away.</summary>
    [Fact]
    public async Task Removing_an_exemption_takes_the_row_away()
    {
        var world = new World(out var entryId);

        await world.RemoveAsync(entryId);

        Assert.Empty(await world.EntriesAsync());
    }

    /// <summary>Removing an entry that does not exist answers not found.</summary>
    [Fact]
    public async Task Removing_an_entry_that_does_not_exist_answers_not_found()
    {
        var world = new World(out _);

        var result = await world.RemoveAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("WhitelistEntryNotFound", result.Error!.Code);
        Assert.Single(await world.EntriesAsync());
    }

    /// <summary>A refused removal is journalled as a failure naming what was probed for.</summary>
    [Fact]
    public async Task A_refused_removal_is_journalled_as_a_failure_naming_what_was_probed_for()
    {
        var world = new World(out _);
        var unknown = Guid.NewGuid();

        await world.RemoveAsync(unknown);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(unknown.ToString(), entry.Subject);
    }

    /// <summary>The seeded row cannot be removed by a caller it is the only thing exempting.</summary>
    /// <remarks>
    /// The sequence that produced the defect, in one test: the installer seeds the address it
    /// arrived on, the administrator signs in from that same address and deletes the row while
    /// tidying, and the whitelist is empty for good — <c>WhitelistSeedRecord</c> blocks re-seeding.
    /// Nothing about the seeded row is special to the handler; what stops this is that the caller is
    /// exempt now and would not be afterwards.
    /// </remarks>
    [Fact]
    public async Task The_row_exempting_the_caller_cannot_be_removed_by_that_caller()
    {
        var world = new World(out var seededId, note: WhitelistSeeder.SeedNote);

        var result = await world.RemoveAsync(seededId, from: InsideOffice);

        Assert.False(result.IsSuccess);
        Assert.Equal("WhitelistEntryProtectsCaller", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Single(await world.EntriesAsync());
    }

    /// <summary>The refusal is journalled as a failure naming the range that was kept.</summary>
    [Fact]
    public async Task The_refusal_is_journalled_as_a_failure_naming_the_range_that_was_kept()
    {
        var world = new World(out var seededId);

        await world.RemoveAsync(seededId, from: InsideOffice);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.FirewallWhitelistChanged, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(Office, entry.Subject);
    }

    /// <summary>
    /// The inverse control: the same row, the same caller, removed once another range covers them.
    /// </summary>
    /// <remarks>
    /// This is the half that proves the guard is about the caller's cover and not about the row. A
    /// guard mutated to refuse every removal — or one pinning the seeded row, or the last row —
    /// passes the refusal test above and fails here. It is also the operator's documented way out,
    /// so a change that made the error message's instructions stop working would be caught.
    /// </remarks>
    [Fact]
    public async Task The_same_row_is_removed_once_another_range_also_covers_the_caller()
    {
        var world = new World(out var seededId);
        await world.AddAsync("203.0.113.0/24", "the whole office network");

        var result = await world.RemoveAsync(seededId, from: InsideOffice);

        Assert.True(result.IsSuccess);
        var cidrs = await world.CidrsAsync();
        Assert.Equal(["203.0.113.0/24"], cidrs);
    }

    /// <summary>A caller elsewhere may remove the last row, stale install address and all.</summary>
    /// <remarks>
    /// The second half of the inverse control, and the reason this guard is not about the last entry
    /// or about the seeded one. The install address is routinely a café network or a jump host that
    /// must never be trusted again; an operator who has moved to the office is exactly the person
    /// who should be able to revoke it, and refusing them would leave the panel pinning a range
    /// belonging to somebody else.
    /// </remarks>
    [Fact]
    public async Task A_caller_elsewhere_may_remove_the_last_remaining_row()
    {
        var world = new World(out var seededId, note: WhitelistSeeder.SeedNote);

        var result = await world.RemoveAsync(seededId, from: Elsewhere);

        Assert.True(result.IsSuccess);
        Assert.Empty(await world.EntriesAsync());
    }

    /// <summary>A caller arriving in the IPv4-mapped spelling is the same caller.</summary>
    /// <remarks>
    /// Kestrel behind a dual-stack socket reports an IPv4 peer as <c>::ffff:203.0.113.7</c>. Compared
    /// as written, that address falls inside no IPv4 range, so the guard would wave through the very
    /// removal it exists to refuse — on the deployment shape where it is most likely to be met.
    /// </remarks>
    [Fact]
    public async Task A_caller_arriving_in_the_mapped_spelling_is_still_refused()
    {
        var world = new World(out var seededId);

        var result = await world.RemoveAsync(seededId, from: "::ffff:203.0.113.7");

        Assert.False(result.IsSuccess);
        Assert.Equal("WhitelistEntryProtectsCaller", result.Error!.Code);
    }

    /// <summary>A caller the panel could not attribute is allowed to remove the row.</summary>
    /// <remarks>
    /// The guard fails open on purpose: it is a lockout guard, not the authorization gate, and
    /// refusing on an address nobody can evaluate would block a legitimate edit to protect a session
    /// that does not exist.
    /// </remarks>
    [Fact]
    public async Task A_caller_whose_address_is_not_an_address_may_still_remove_the_row()
    {
        var world = new World(out var seededId);

        var result = await world.RemoveAsync(seededId, from: "unknown");

        Assert.True(result.IsSuccess);
        Assert.Empty(await world.EntriesAsync());
    }

    /// <summary>The store, the journal and the handler under test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database this world's contexts share.</summary>
        private readonly string _store = Guid.NewGuid().ToString();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Seeds one exemption and hands back its identifier.</summary>
        /// <param name="entryId">The seeded row's identity.</param>
        /// <param name="note">The note the row carries; the installer's, when a test says so.</param>
        public World(out Guid entryId, string note = "office")
        {
            var entry = new WhitelistEntry(Guid.NewGuid(), Office, note, Now);
            entryId = entry.Id;

            using var context = FirewallTestContext.Create(_store);
            context.WhitelistEntries.Add(entry);
            context.SaveChanges();
        }

        /// <summary>Adds a second exemption, the way an administrator would before removing one.</summary>
        /// <param name="cidr">The range to exempt.</param>
        /// <param name="note">What it is.</param>
        public async Task AddAsync(string cidr, string note)
        {
            using var context = FirewallTestContext.Create(_store);
            context.WhitelistEntries.Add(new WhitelistEntry(Guid.NewGuid(), cidr, note, Now));
            await context.SaveChangesAsync();
        }

        /// <summary>Runs the handler once, on its own context, the way a request does.</summary>
        /// <param name="entryId">The row to remove.</param>
        /// <param name="from">The address the request arrives from.</param>
        public async Task<Result<bool>> RemoveAsync(Guid entryId, string from = Elsewhere)
        {
            using var context = FirewallTestContext.Create(_store);
            var handler = new RemoveWhitelistEntryCommandHandler(
                context,
                new WhitelistGuard(context),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()));

            return await handler.HandleAsync(
                new RemoveWhitelistEntryCommand(entryId, from, "curl"), CancellationToken.None);
        }

        /// <summary>Reads every whitelist row the store holds.</summary>
        public async Task<List<WhitelistEntry>> EntriesAsync()
        {
            using var context = FirewallTestContext.Create(_store);
            return await context.WhitelistEntries.AsNoTracking().ToListAsync();
        }

        /// <summary>Reads the ranges the store still exempts.</summary>
        public async Task<List<string>> CidrsAsync()
        {
            using var context = FirewallTestContext.Create(_store);
            return await context.WhitelistEntries.AsNoTracking()
                .Select(entry => entry.Cidr)
                .ToListAsync();
        }
    }
}
