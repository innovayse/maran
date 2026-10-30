using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The count this module hands the Accounts module's resource summary: it counts the named
/// account's rows and nobody else's.
/// </summary>
public sealed class BackupAccountResourceCounterTests
{
    /// <summary>Only the named accounts rows are counted.</summary>
    [Fact]
    public async Task Only_the_named_accounts_rows_are_counted()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await using var context = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        context.Backups.Add(BackupsTestContext.CompletedRow(mine));
        // The copy taken before a deletion is a backup the panel holds, so it counts.
        context.Backups.Add(BackupsTestContext.CompletedRow(mine, kind: BackupKind.PreDeletion));
        context.Backups.Add(BackupsTestContext.CompletedRow(theirs));
        await context.SaveChangesAsync();

        var count = await new BackupAccountResourceCounter(context).CountAsync(mine, CancellationToken.None);

        Assert.Equal(AccountResourceKind.Backups, count.Kind);
        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(2, count.Used);
    }

    /// <summary>An account with no rows is counted as zero, not as unavailable.</summary>
    [Fact]
    public async Task An_account_with_no_rows_is_counted_as_zero_not_as_unavailable()
    {
        await using var context = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());

        var count = await new BackupAccountResourceCounter(context).CountAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AccountResourceCountState.Counted, count.State);
        Assert.Equal(0, count.Used);
    }
}
