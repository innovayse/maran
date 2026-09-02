using Maran.Modules.Accounts.Commands.ReactivateAccount;
using Maran.Modules.Accounts.Commands.SuspendAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.SuspendAccount;

/// <summary>
/// Suspension and its reversal, exercised together: the pair is one behaviour — an account can be
/// turned off and back on without losing anything — and testing either alone would leave the state
/// machine half-covered.
/// </summary>
public sealed class AccountSuspensionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The caller address every command in this file carries.</summary>
    private const string Ip = "203.0.113.7";

    /// <summary>The user agent every command in this file carries.</summary>
    private const string Client = "unit-tests";

    private readonly AccountsDbContext _context = CreateDbContext();
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Builds a journal writing into a writer nothing asserts on; the audit tests do that.</summary>
    private static AccountAuditJournal Journal()
    {
        return new AccountAuditJournal(new RecordingAuditWriter(), FakeCurrentUser.Admin());
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<Account> SeedAsync()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Now);
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    /// <summary>Suspending an active account moves it into suspension.</summary>
    [Fact]
    public async Task Suspending_an_active_account_moves_it_into_suspension()
    {
        var account = await SeedAsync();

        var result = await new SuspendAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
        Assert.Equal(AccountStatus.Suspended, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>Suspending an already suspended account is a no op rather than a failure.</summary>
    [Fact]
    public async Task Suspending_an_already_suspended_account_is_a_no_op_rather_than_a_failure()
    {
        // A billing system calls this on every overdue invoice; an error on the second call would
        // make the caller track state the panel already holds.
        var account = await SeedAsync();
        var handler = new SuspendAccountCommandHandler(_context, _agent, Journal());
        await handler.HandleAsync(new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        var result = await handler.HandleAsync(
            new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
    }

    /// <summary>Reactivating a suspended account puts it back.</summary>
    [Fact]
    public async Task Reactivating_a_suspended_account_puts_it_back()
    {
        var account = await SeedAsync();
        await new SuspendAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        var result = await new ReactivateAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new ReactivateAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        Assert.Equal(AccountStatus.Active, result.Value.Status);
    }

    /// <summary>Suspending keeps everything else about the account.</summary>
    [Fact]
    public async Task Suspending_keeps_everything_else_about_the_account()
    {
        var account = await SeedAsync();

        await new SuspendAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        var stored = await _context.Accounts.SingleAsync();
        Assert.Equal("acme", stored.Name);
        Assert.Equal("acme.example.com", stored.PrimaryDomain);
        Assert.Equal(Now, stored.CreatedAt);
    }

    /// <summary>Suspending an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Suspending_an_account_that_does_not_exist_answers_not_found()
    {
        var result = await new SuspendAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new SuspendAccountCommand(Guid.NewGuid(), Ip, Client), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Reactivating an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Reactivating_an_account_that_does_not_exist_answers_not_found()
    {
        var result = await new ReactivateAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new ReactivateAccountCommand(Guid.NewGuid(), Ip, Client), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Suspending asks the agent to stop the account by its system name.</summary>
    [Fact]
    public async Task Suspending_asks_the_agent_to_stop_the_account_by_its_system_name()
    {
        var account = await SeedAsync();

        await new SuspendAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        Assert.Equal(["suspend:acme"], _agent.Calls);
    }

    /// <summary>An agent that refuses leaves the row untouched.</summary>
    [Fact]
    public async Task An_agent_that_refuses_leaves_the_row_untouched()
    {
        // The order is the whole subject: the row records what the agent did, so a refusal
        // must not leave the panel claiming an account is suspended while its sites serve.
        var account = await SeedAsync();
        var refusing = new RecordingAgentAccountsClient(Error.Of("AgentSystemFailure"));

        var result = await new SuspendAccountCommandHandler(_context, refusing, Journal()).HandleAsync(
            new SuspendAccountCommand(account.Id, Ip, Client), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An account that does not exist never reaches the agent.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_never_reaches_the_agent()
    {
        await new SuspendAccountCommandHandler(_context, _agent, Journal()).HandleAsync(
            new SuspendAccountCommand(Guid.NewGuid(), Ip, Client), CancellationToken.None);

        Assert.Empty(_agent.Calls);
    }
}
