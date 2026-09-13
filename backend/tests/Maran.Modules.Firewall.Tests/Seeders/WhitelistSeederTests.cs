using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Seeders;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Firewall.Tests.Seeders;

/// <summary>
/// What the installer's recorded address becomes, and the promise <c>panel.env</c> makes about it:
/// read once, for the life of the server, and never again.
/// </summary>
public sealed class WhitelistSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The installers address becomes the first whitelist row.</summary>
    [Fact]
    public async Task The_installers_address_becomes_the_first_whitelist_row()
    {
        // An empty whitelist on day one is a server whose only administrator can lock themselves
        // out of it with a typo, from a console they were probably not sitting at.
        using var context = FirewallTestContext.Create();

        var seeded = await SeedAsync(context, "203.0.113.7/32");

        Assert.True(seeded);
        var entry = Assert.Single(await context.WhitelistEntries.AsNoTracking().ToListAsync());
        Assert.Equal("203.0.113.7/32", entry.Cidr);
        Assert.Equal(WhitelistSeeder.SeedNote, entry.Note);
        Assert.Equal(Now, entry.CreatedAt);
    }

    /// <summary>A seed reported in the dual stack mapped form still exempts the operator.</summary>
    [Theory]
    [InlineData("::ffff:203.0.113.7/128", "203.0.113.7/32")]
    [InlineData("::ffff:203.0.113.0/120", "203.0.113.0/24")]
    public async Task A_seed_reported_in_the_dual_stack_mapped_form_still_exempts_the_operator(
        string seed, string stored)
    {
        // The failure this closes, end to end: on a host whose sshd listens on :: with IPv4-mapped
        // sockets, SSH_CLIENT is ::ffff:203.0.113.7, the installer records that and prints "Seeding
        // the firewall whitelist with ::ffff:203.0.113.7/128" — and a panel that refused the value
        // came up with an empty whitelist, one warning line in a boot log, and an operator who had
        // been told the opposite. Refusal is right where a 400 reaches a person; here nobody is
        // listening, so the value is translated to the plain form it means.
        using var context = FirewallTestContext.Create();

        Assert.True(await SeedAsync(context, seed));

        var entry = Assert.Single(await context.WhitelistEntries.AsNoTracking().ToListAsync());
        Assert.Equal(stored, entry.Cidr);
    }

    /// <summary>The stored seed matches the address the panel will compare against it.</summary>
    [Fact]
    public async Task The_stored_seed_matches_the_address_the_panel_will_compare_against_it()
    {
        // The assertion above is about a string; this one is about the point of the whole feature.
        // A mapped RANGE stored verbatim parses, is listed back to the operator, and matches nobody,
        // because IpAddressNormalizer turns the address it is compared against into plain IPv4 and
        // IPNetwork.Contains is false across families.
        using var context = FirewallTestContext.Create();
        await SeedAsync(context, "::ffff:203.0.113.7/128");

        var entry = Assert.Single(await context.WhitelistEntries.AsNoTracking().ToListAsync());

        Assert.True(IpAddressNormalizer.TryNormalize("::ffff:203.0.113.7", out var address));
        Assert.True(entry.Covers(address));
    }

    /// <summary>The seed is ignored once the whitelist has a row.</summary>
    [Fact]
    public async Task The_seed_is_ignored_once_the_whitelist_has_a_row()
    {
        using var context = FirewallTestContext.Create();
        context.WhitelistEntries.Add(new WhitelistEntry(Guid.NewGuid(), "198.51.100.0/24", "office", Now));
        await context.SaveChangesAsync();

        var seeded = await SeedAsync(context, "203.0.113.7/32");

        Assert.False(seeded);
        Assert.Single(await context.WhitelistEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>A seed that has been read once is not read again when the whitelist is emptied.</summary>
    [Fact]
    public async Task A_seed_that_has_been_read_once_is_not_read_again_when_the_whitelist_is_emptied()
    {
        // panel.env promises an operator in as many words that editing the value afterwards changes
        // nothing. Gating on an empty whitelist did not keep that promise: deleting the seeded row
        // empties the whitelist, so the next restart restored an exemption somebody had deliberately
        // revoked — and the seed is routinely a shared office NAT egress or a jump host, which is
        // exactly why it gets revoked, and whose address belongs to a stranger a year later.
        using var context = FirewallTestContext.Create();
        Assert.True(await SeedAsync(context, "203.0.113.7/32"));

        context.WhitelistEntries.RemoveRange(await context.WhitelistEntries.ToListAsync());
        await context.SaveChangesAsync();

        Assert.False(await SeedAsync(context, "203.0.113.7/32"));
        Assert.Empty(await context.WhitelistEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>The seed is journalled, because nobody signed in created that exemption.</summary>
    [Fact]
    public async Task The_seed_is_journalled_because_nobody_signed_in_created_that_exemption()
    {
        // The panel exempts an address from every automatic ban, at its own initiative, with no
        // request behind it. Without this entry the whitelist's first row is the only one in it with
        // no history — and the journal is also where the fact survives, since the row itself can be
        // deleted and the append-only journal cannot.
        using var context = FirewallTestContext.Create();
        var audit = new RecordingAuditWriter();

        Assert.True(await SeedAsync(context, "203.0.113.7/32", audit));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.FirewallWhitelistSeeded, entry.Action);
        Assert.Equal("203.0.113.7/32", entry.Subject);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(FirewallAuditJournal.SystemActor, entry.ActorUsername);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A seed that is not a range is not stored.</summary>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("203.0.113.7/24")]
    [InlineData("fe80::1%eth0/128")]
    [InlineData("::ffff:203.0.113.0/119")]
    public async Task A_seed_that_is_not_a_range_is_not_stored(string seed)
    {
        // A malformed row matches no packet that ever arrives, so it would tell its reader they were
        // exempt while they were not — which is worse than the empty whitelist it replaces. The last
        // case is a mapped range with a prefix shorter than the mapped block: it does not parse at
        // all, which is why the translation above needs no rule for one.
        using var context = FirewallTestContext.Create();

        var seeded = await SeedAsync(context, seed);

        Assert.False(seeded);
        Assert.Empty(await context.WhitelistEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>A seed the panel cannot use is retried on the next boot rather than marked as read.</summary>
    [Fact]
    public async Task A_seed_the_panel_cannot_use_is_retried_on_the_next_boot_rather_than_marked_as_read()
    {
        // The warning is worth repeating every boot: nothing else tells an operator the server is
        // unprotected, and marking an unusable value as "read" would silence the one line that does.
        using var context = FirewallTestContext.Create();

        Assert.False(await SeedAsync(context, "not-a-range"));

        Assert.Empty(await context.WhitelistSeedRecords.AsNoTracking().ToListAsync());
        Assert.True(await SeedAsync(context, "203.0.113.7/32"));
    }

    /// <summary>An install that saw no client address writes nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_install_that_saw_no_client_address_writes_nothing(string seed)
    {
        // A console install genuinely has no client address; the installer ends with a warning
        // saying so, and there is nothing here to do.
        using var context = FirewallTestContext.Create();

        Assert.False(await SeedAsync(context, seed));
        Assert.Empty(await context.WhitelistEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>Runs the seeder once against <paramref name="context"/>.</summary>
    /// <param name="context">The store to seed.</param>
    /// <param name="seed">The value the installer wrote into <c>Firewall__SeedWhitelistCidr</c>.</param>
    /// <param name="audit">Where the journal entry goes, when a test asserts on it.</param>
    private static async Task<bool> SeedAsync(
        FirewallDbContext context, string seed, RecordingAuditWriter? audit = null)
    {
        var seeder = new WhitelistSeeder(
            context,
            new FakeClock(Now),
            new FirewallAuditJournal(audit ?? new RecordingAuditWriter(), new FakeCurrentUser()),
            NullLogger<WhitelistSeeder>.Instance);

        return await seeder.SeedAsync(
            new FirewallOptions { SshPorts = "22", PanelPort = 8443, SeedWhitelistCidr = seed },
            CancellationToken.None);
    }
}
