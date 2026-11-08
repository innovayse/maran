using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Commands.ReactivateAccount;

/// <summary>
/// The agent keeps no record of the shell an account was given and returns <c>nologin</c> on every
/// unsuspension unless told otherwise (issue #194), so the reactivation states the shell the account
/// was created with.
/// </summary>
public sealed class ReactivateAccountShellTests : IDisposable
{
    /// <summary>The shared fixture: context, agent double and the handlers.</summary>
    private readonly AccountSuspensionHarness _harness = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>Seeds a suspended account.</summary>
    /// <param name="shellAccess">Whether it was created with a login shell.</param>
    /// <returns>The account.</returns>
    private async Task<Account> SeedSuspendedAsync(bool shellAccess)
    {
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), AccountSuspensionHarness.Now, shellAccess: shellAccess);
        account.Suspend();
        _harness.Context.Accounts.Add(account);
        await _harness.Context.SaveChangesAsync();
        return account;
    }

    /// <summary>An account created with a shell gets it back when it is reactivated.</summary>
    [Fact]
    public async Task An_account_created_with_a_shell_gets_it_back_when_it_is_reactivated()
    {
        var account = await SeedSuspendedAsync(shellAccess: true);

        var result = await _harness.ReactivateAsync(_harness.Agent, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(("acme", AccountLoginShell.Bash), Assert.Single(_harness.Agent.Unsuspended));
    }

    /// <summary>An account created without a shell is reactivated with none: the control for the case above.</summary>
    [Fact]
    public async Task An_account_created_without_a_shell_is_reactivated_with_none()
    {
        var account = await SeedSuspendedAsync(shellAccess: false);

        var result = await _harness.ReactivateAsync(_harness.Agent, new StubMessageBus(), account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(("acme", AccountLoginShell.Nologin), Assert.Single(_harness.Agent.Unsuspended));
    }
}
