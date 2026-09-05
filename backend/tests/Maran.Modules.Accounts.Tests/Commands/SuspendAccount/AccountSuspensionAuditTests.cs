using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.SuspendAccount;

/// <summary>
/// What suspending and reactivating an account leave in the audit journal. The pair is tested
/// together because it is one behaviour, and because the panel afterwards shows only the state —
/// the journal is the only record of who moved it and when.
/// </summary>
public sealed class AccountSuspensionAuditTests : IDisposable
{
    private const string Ip = "203.0.113.7";
    private const string Client = "unit-tests";

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly AccountsDbContext _context = CreateDbContext();
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A suspension is journalled as a success naming the account.</summary>
    [Fact]
    public async Task A_suspension_is_journalled_as_a_success_naming_the_account()
    {
        var account = await SeedAsync();

        await SuspendAsync(new RecordingAgentAccountsClient(), account.Id);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountSuspended, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(Ip, entry.IpAddress);
        Assert.Equal(Client, entry.UserAgent);
    }

    /// <summary>A reactivation is journalled as a success naming the account.</summary>
    [Fact]
    public async Task A_reactivation_is_journalled_as_a_success_naming_the_account()
    {
        var account = await SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        await SuspendAsync(agent, account.Id);
        _audit.Entries.Clear();

        await new ReactivateAccountCommandHandler(_context, agent, Journal()).HandleAsync(
            new ReactivateAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountReactivated, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A suspension the agent refuses is journalled as a failure and never as a success.</summary>
    [Fact]
    public async Task A_suspension_the_agent_refuses_is_journalled_as_a_failure_and_never_as_a_success()
    {
        var account = await SeedAsync();

        var result = await SuspendAsync(new RecordingAgentAccountsClient(Error.Of("AgentSystemFailure", ErrorType.Failure)), account.Id);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountSuspended, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A suspension of an unknown account is journalled naming what was probed for.</summary>
    [Fact]
    public async Task A_suspension_of_an_unknown_account_is_journalled_naming_what_was_probed_for()
    {
        // "Not found" is also the answer another tenant's identifier gets, so the failure entry is
        // what makes a run of probes visible.
        var probed = Guid.NewGuid();

        var result = await SuspendAsync(new RecordingAgentAccountsClient(), probed);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountSuspended, entry.Action);
        Assert.Equal(probed.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A reactivation of an unknown account is journalled naming what was probed for.</summary>
    [Fact]
    public async Task A_reactivation_of_an_unknown_account_is_journalled_naming_what_was_probed_for()
    {
        var probed = Guid.NewGuid();

        var result = await new ReactivateAccountCommandHandler(
            _context, new RecordingAgentAccountsClient(), Journal()).HandleAsync(
            new ReactivateAccountCommand(probed, Ip, Client), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AccountReactivated, entry.Action);
        Assert.Equal(probed.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Builds a fresh, isolated in-memory context.</summary>
    /// <returns>The context.</returns>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>The journal under test, writing into this fixture's recorder.</summary>
    /// <returns>The journal.</returns>
    private AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(_audit, FakeCurrentUser.Admin());
    }

    /// <summary>Seeds one active account named "acme".</summary>
    /// <returns>The seeded account.</returns>
    private async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    /// <summary>Runs one suspension.</summary>
    /// <param name="agent">The agent double to answer with.</param>
    /// <param name="accountId">The account to suspend.</param>
    /// <returns>The handler's result.</returns>
    private Task<Result<AccountDto>> SuspendAsync(RecordingAgentAccountsClient agent, Guid accountId)
    {
        return new SuspendAccountCommandHandler(_context, agent, Journal()).HandleAsync(
            new SuspendAccountCommand(accountId, Ip, Client), CancellationToken.None);
    }
}
