using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Queries.ListWhitelist;
using Maran.Modules.Firewall.Tests.TestSupport;

namespace Maran.Modules.Firewall.Tests.Queries.ListWhitelist;

/// <summary>What the whitelist screen is shown.</summary>
public sealed class ListWhitelistQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every exempt range is listed oldest first with the note it was added under.</summary>
    [Fact]
    public async Task Every_exempt_range_is_listed_oldest_first_with_the_note_it_was_added_under()
    {
        // Oldest first because the first row is usually the installer's seed, and an operator
        // reading this screen is asking "why am I exempt" before "who else is".
        using var context = FirewallTestContext.Create();
        context.WhitelistEntries.AddRange(
            new WhitelistEntry(Guid.NewGuid(), "198.51.100.0/24", "monitoring", Now.AddDays(-1)),
            new WhitelistEntry(Guid.NewGuid(), "203.0.113.7/32", "office", Now.AddDays(-2)));
        await context.SaveChangesAsync();

        var result = await new ListWhitelistQueryHandler(context)
            .HandleAsync(new ListWhitelistQuery(), CancellationToken.None);

        Assert.Equal(["203.0.113.7/32", "198.51.100.0/24"], result.Value.Select(entry =>
        {
            return entry.Cidr;
        }));
        Assert.Equal("office", result.Value[0].Note);
    }

    /// <summary>An empty whitelist lists nothing rather than failing.</summary>
    [Fact]
    public async Task An_empty_whitelist_lists_nothing_rather_than_failing()
    {
        using var context = FirewallTestContext.Create();

        var result = await new ListWhitelistQueryHandler(context)
            .HandleAsync(new ListWhitelistQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
