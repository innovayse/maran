using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

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

    /// <summary>A key's entry names the key as the actor, with no user id, and never the secret.</summary>
    [Fact]
    public async Task A_keys_entry_names_the_key_as_the_actor_with_no_user_id()
    {
        var keyId = Guid.NewGuid();
        var caller = new ApiKeyCaller(keyId, [ApiKeyScopes.AccountsRead], Guid.NewGuid());
        var journal = new AccountAuditJournal(_writer, new FakeCurrentUser(caller));

        await journal.RecordSuccessAsync(
            AccountAuditActions.AccountSuspended, "acme", "203.0.113.7", "agent", CancellationToken.None);
        await journal.RecordFailureAsync(
            AccountAuditActions.AccountSuspended, "acme", "203.0.113.7", "agent", CancellationToken.None);

        Assert.Equal(2, _writer.Entries.Count);
        Assert.All(_writer.Entries, entry =>
        {
            Assert.Equal(Guid.Empty, entry.ActorUserId);
            Assert.Equal("api-key:" + keyId.ToString("N"), entry.ActorUsername);
            Assert.DoesNotContain("Bearer", entry.ActorUsername, StringComparison.Ordinal);
        });
    }

    /// <summary>The inverse control: a reseller's login is recorded under its user id and login name.</summary>
    [Fact]
    public async Task A_resellers_login_is_recorded_under_its_user_id_and_name()
    {
        var caller = new ResellerCaller(Guid.NewGuid(), "reseller-login", Guid.NewGuid());
        var journal = new AccountAuditJournal(_writer, new FakeCurrentUser(caller));

        await journal.RecordSuccessAsync(
            AccountAuditActions.AccountSuspended, "acme", "203.0.113.7", "agent", CancellationToken.None);

        var entry = Assert.Single(_writer.Entries);
        Assert.Equal(caller.UserId, entry.ActorUserId);
        Assert.Equal("reseller-login", entry.ActorUsername);
    }

    /// <summary>Creates the journal under test over the recording double.</summary>
    /// <returns>The journal.</returns>
    private AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(_writer, FakeCurrentUser.Admin());
    }
}
