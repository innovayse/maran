using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Services;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ftp.Tests.Services;

/// <summary>
/// The count this module hands the Accounts module's resource summary: it counts the named
/// account's rows and nobody else's.
/// </summary>
public sealed class FtpAccountResourceCounterTests
{
    /// <summary>Only the named accounts rows are counted.</summary>
    [Fact]
    public async Task Only_the_named_accounts_rows_are_counted()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        using var world = new FtpTestContext();
        var context = world.Database;
        context.FtpUsers.Add(new FtpUser(Guid.NewGuid(), mine, "one", "alice_one", world.Clock.UtcNow));
        context.FtpUsers.Add(new FtpUser(Guid.NewGuid(), mine, "two", "alice_two", world.Clock.UtcNow));
        context.FtpUsers.Add(new FtpUser(Guid.NewGuid(), theirs, "one", "bob_one", world.Clock.UtcNow));
        await context.SaveChangesAsync();

        var count = await new FtpAccountResourceCounter(context).CountAsync(mine, CancellationToken.None);

        Assert.Equal(AccountResourceKind.FtpLogins, count.Kind);
        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(2, count.Used);
    }

    /// <summary>An account with no rows is counted as zero, not as unavailable.</summary>
    [Fact]
    public async Task An_account_with_no_rows_is_counted_as_zero_not_as_unavailable()
    {
        using var world = new FtpTestContext();
        var context = world.Database;

        var count = await new FtpAccountResourceCounter(context).CountAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(0, count.Used);
    }
}
