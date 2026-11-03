using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.SuspendAccount;

/// <summary>
/// Suspension and its reversal, exercised together: the pair is one behaviour — an account can be
/// turned off and back on without losing anything — and testing either alone would leave the state
/// machine half-covered. The refusals the host's observation produces are in
/// <see cref="AccountSuspensionAttestationTests"/>, and the words a task line carries in
/// <see cref="AccountSuspensionTaskReportTests"/>.
/// </summary>
public sealed class AccountSuspensionTests : IDisposable
{
    /// <summary>The shared fixture: context, doubles and the seed, suspend and reactivate helpers.</summary>
    private readonly AccountSuspensionHarness _harness = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>Suspending an active account moves it into suspension.</summary>
    [Fact]
    public async Task Suspending_an_active_account_moves_it_into_suspension()
    {
        var account = await _harness.SeedAsync();

        var result = await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
        Assert.Equal(AccountStatus.Suspended, (await _harness.Context.Accounts.SingleAsync()).Status);
    }

    /// <summary>Suspending an already suspended account is a no op rather than a failure.</summary>
    [Fact]
    public async Task Suspending_an_already_suspended_account_is_a_no_op_rather_than_a_failure()
    {
        // A billing system calls this on every overdue invoice; an error on the second call would
        // make the caller track state the panel already holds.
        var account = await _harness.SeedAsync();
        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var result = await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
    }

    /// <summary>Reactivating a suspended account puts it back.</summary>
    [Fact]
    public async Task Reactivating_a_suspended_account_puts_it_back()
    {
        var account = await _harness.SeedAsync();
        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var result = await _harness.ReactivateAsync(_harness.Agent, new StubMessageBus(), account.Id);

        Assert.Equal(AccountStatus.Active, result.Value.Status);
    }

    /// <summary>Suspending keeps everything else about the account.</summary>
    [Fact]
    public async Task Suspending_keeps_everything_else_about_the_account()
    {
        var account = await _harness.SeedAsync();

        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), account.Id);

        var stored = await _harness.Context.Accounts.SingleAsync();
        Assert.Equal("acme", stored.Name);
        Assert.Equal("acme.example.com", stored.PrimaryDomain);
        Assert.Equal(AccountSuspensionHarness.Now, stored.CreatedAt);
    }

    /// <summary>Suspending an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Suspending_an_account_that_does_not_exist_answers_not_found()
    {
        var result = await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), Guid.NewGuid());

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Reactivating an account that does not exist answers not found.</summary>
    [Fact]
    public async Task Reactivating_an_account_that_does_not_exist_answers_not_found()
    {
        var result = await _harness.ReactivateAsync(_harness.Agent, new StubMessageBus(), Guid.NewGuid());

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Suspending asks every module to stop before it touches the host.</summary>
    /// <remarks>
    /// The order is the whole of the safety, exactly as it is for deletion: the cascade is invoked
    /// while the account is still active, so a module that refuses leaves nothing half-done.
    /// </remarks>
    [Fact]
    public async Task Suspending_asks_every_module_to_stop_before_it_touches_the_host()
    {
        var account = await _harness.SeedAsync();
        var bus = new StubMessageBus();

        await _harness.SuspendAsync(_harness.Agent, bus, account.Id);

        var announced = Assert.IsType<AccountSuspending>(Assert.Single(bus.Invoked));
        Assert.Equal(account.Id, announced.AccountId);
        Assert.Equal("acme", announced.Username);
        Assert.Equal(["suspend:acme", "observe:acme"], _harness.Agent.Calls);
    }

    /// <summary>A module that refuses to stop leaves the account active and the host untouched.</summary>
    [Fact]
    public async Task A_module_that_refuses_to_stop_leaves_the_account_active_and_the_host_untouched()
    {
        var account = await _harness.SeedAsync();
        var refusing = new StubMessageBus(new InvalidOperationException("the vhost could not be stubbed"));

        var result = await _harness.SuspendAsync(_harness.Agent, refusing, account.Id);

        Assert.Equal("AccountSuspensionFailed", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Equal(AccountStatus.Active, (await _harness.Context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An agent that refuses leaves the row untouched.</summary>
    [Fact]
    public async Task An_agent_that_refuses_leaves_the_row_untouched()
    {
        // The order is the whole subject: the row records what the agent did, so a refusal
        // must not leave the panel claiming an account is suspended while its sites serve.
        var account = await _harness.SeedAsync();
        var refusing = new RecordingAgentAccountsClient(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await _harness.SuspendAsync(refusing, new StubMessageBus(), account.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(AccountStatus.Active, (await _harness.Context.Accounts.SingleAsync()).Status);
    }

    /// <summary>An account that does not exist never reaches the agent.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_never_reaches_the_agent()
    {
        await _harness.SuspendAsync(_harness.Agent, new StubMessageBus(), Guid.NewGuid());

        Assert.Empty(_harness.Agent.Calls);
    }
}
