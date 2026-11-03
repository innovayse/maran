using Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountOwnerEmail;

/// <summary>
/// Changing an account's owner address as a reseller: only its own accounts exist for it, a suspended
/// reseller cannot, and an administrator can for a reseller's account.
/// </summary>
public sealed class ChangeAccountOwnerEmailResellerTests : IDisposable
{
    /// <summary>The world of two resellers, each with an account.</summary>
    private readonly ResellerAccountsWorld _world = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the login's change is requested on; the login accepts.</summary>
    private readonly StubMessageBus _bus = new()
    {
        Responder = _ =>
        {
            return Result.Ok();
        },
    };

    /// <inheritdoc />
    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>Changes an account's address as <paramref name="caller"/>.</summary>
    /// <param name="caller">The caller the change runs as.</param>
    /// <param name="accountId">The account.</param>
    /// <returns>The handler's result.</returns>
    private async Task<Result<AccountDetailDto>> ChangeAsync(ICurrentUser caller, Guid accountId)
    {
        var handler = AccountHandlerFactory.ChangeOwnerEmail(_world.ContextFor(caller), caller, _bus, _audit);

        return await handler.HandleAsync(
            new ChangeAccountOwnerEmailCommand("new@example.com", accountId, "203.0.113.7", "unit-tests"),
            CancellationToken.None);
    }

    /// <summary>A reseller changes its own account's address and the answer names the reseller.</summary>
    [Fact]
    public async Task A_reseller_changes_its_own_accounts_address()
    {
        var result = await ChangeAsync(_world.UserA, _world.AccountA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("new@example.com", result.Value.OwnerEmail);
        Assert.Equal("alpha", result.Value.ResellerName);
        var entry = Assert.Single(_audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("alpha-login", entry.ActorUsername);
        Assert.Equal(AccountAuditActions.AccountOwnerEmailChanged, entry.Action);
    }

    /// <summary>A reseller changing another reseller's account is answered as for a missing account and never asks the login.</summary>
    [Fact]
    public async Task A_reseller_changing_another_resellers_account_is_answered_as_for_a_missing_one()
    {
        var foreign = await ChangeAsync(_world.UserA, _world.AccountB.Id);
        var missing = await ChangeAsync(_world.UserA, Guid.NewGuid());

        Assert.Equal("AccountNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(missing.Error!.Code, foreign.Error.Code);
        Assert.Empty(_bus.Invoked);
        var stored = await _world.ContextFor(FakeCurrentUser.Admin()).Accounts.AsNoTracking()
            .SingleAsync(a => a.Id == _world.AccountB.Id);
        Assert.Equal("bravo@example.com", stored.OwnerEmail);
    }

    /// <summary>A suspended reseller's live token cannot change an address and never asks the login.</summary>
    [Fact]
    public async Task A_suspended_resellers_live_token_cannot_change_an_address()
    {
        var admin = _world.ContextFor(FakeCurrentUser.Admin());
        (await admin.Resellers.SingleAsync(r => r.Id == _world.ResellerA.Id)).Suspend();
        await admin.SaveChangesAsync();

        var result = await ChangeAsync(_world.UserA, _world.AccountA.Id);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_bus.Invoked);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>An administrator changes a reseller's account's address and the answer names the reseller.</summary>
    [Fact]
    public async Task An_administrator_changes_a_resellers_accounts_address()
    {
        var result = await ChangeAsync(FakeCurrentUser.Admin(), _world.AccountB.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("bravo", result.Value.ResellerName);
        Assert.Equal("new@example.com", result.Value.OwnerEmail);
    }
}
