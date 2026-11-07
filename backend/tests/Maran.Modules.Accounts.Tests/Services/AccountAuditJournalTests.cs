using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// Which hosting account <see cref="AccountAuditJournal"/> puts on an entry: the one the caller
/// passes, and none when the caller passes none — as the deletion flow does on purpose.
/// </summary>
public sealed class AccountAuditJournalTests
{
    /// <summary>The recording double the journal writes into.</summary>
    private readonly RecordingAuditWriter _writer = new();

    /// <summary>A success entry carries the account its caller passed.</summary>
    [Fact]
    public async Task A_success_entry_carries_the_account_its_caller_passed()
    {
        var accountId = Guid.NewGuid();

        await Journal().RecordSuccessAsync(
            AccountAuditActions.AccountSuspended, "acme", "203.0.113.7", "agent", CancellationToken.None, accountId);

        var entry = Assert.Single(_writer.Entries);
        Assert.Equal(accountId, entry.AccountId);
        Assert.True(entry.Succeeded);
        Assert.Equal("acme", entry.Subject);
    }

    /// <summary>A failure entry carries the account its caller passed.</summary>
    [Fact]
    public async Task A_failure_entry_carries_the_account_its_caller_passed()
    {
        var accountId = Guid.NewGuid();

        await Journal().RecordFailureAsync(
            AccountAuditActions.AccountSuspended, "acme", "203.0.113.7", "agent", CancellationToken.None, accountId);

        var entry = Assert.Single(_writer.Entries);
        Assert.Equal(accountId, entry.AccountId);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An entry whose caller passes no account carries none — the inverse control, and how a deletion is recorded.</summary>
    [Fact]
    public async Task An_entry_whose_caller_passes_no_account_carries_none()
    {
        await Journal().RecordSuccessAsync(
            AccountAuditActions.AccountDeleted, "acme", "203.0.113.7", "agent", CancellationToken.None);
        await Journal().RecordFailureAsync(
            AccountAuditActions.AccountDeleted, "acme", "203.0.113.7", "agent", CancellationToken.None);

        Assert.All(_writer.Entries, entry =>
        {
            Assert.Null(entry.AccountId);
        });
        Assert.Equal(2, _writer.Entries.Count);
    }

    /// <summary>Creates the journal under test over the recording double.</summary>
    /// <returns>The journal.</returns>
    private AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(_writer, FakeCurrentUser.Admin());
    }
}
