using Maran.Modules.Sftp.Services;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sftp.Tests.Services;

/// <summary>
/// The count this module hands the Accounts module's resource summary: it counts the named
/// account's rows and nobody else's.
/// </summary>
public sealed class SftpAccountResourceCounterTests
{
    /// <summary>Only the named accounts rows are counted.</summary>
    [Fact]
    public async Task Only_the_named_accounts_rows_are_counted()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await using var context = SftpTestContext.Create(FakeCurrentUser.Admin());
        context.SftpUsers.Add(SftpTestContext.Row(mine, "alice", "one"));
        context.SftpUsers.Add(SftpTestContext.Row(mine, "alice", "two"));
        context.SftpUsers.Add(SftpTestContext.Row(theirs, "bob", "one"));
        await context.SaveChangesAsync();

        var count = await new SftpAccountResourceCounter(context).CountAsync(mine, CancellationToken.None);

        Assert.Equal(AccountResourceKind.SftpLogins, count.Kind);
        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(2, count.Used);
    }

    /// <summary>An account with no rows is counted as zero, not as unavailable.</summary>
    [Fact]
    public async Task An_account_with_no_rows_is_counted_as_zero_not_as_unavailable()
    {
        await using var context = SftpTestContext.Create(FakeCurrentUser.Admin());

        var count = await new SftpAccountResourceCounter(context).CountAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(0, count.Used);
    }
}
