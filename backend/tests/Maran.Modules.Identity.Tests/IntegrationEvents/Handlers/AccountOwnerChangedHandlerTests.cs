using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Contract of <see cref="AccountOwnerChangedHandler"/>: when an account changes hands, the view-as
/// sessions its former reseller holds on it end, and nothing else does.
/// </summary>
public sealed class AccountOwnerChangedHandlerTests : IDisposable
{
    /// <summary>The fixture holding the context, the clock and the seeding helpers.</summary>
    private readonly ViewAsFixture _fixture = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _fixture.Dispose();
    }

    /// <summary>Builds the handler over the fixture's context and clock.</summary>
    /// <returns>The handler.</returns>
    private AccountOwnerChangedHandler NewHandler()
    {
        var sessions = new SessionService(
            _fixture.Context,
            _fixture.Clock,
            new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
        return new AccountOwnerChangedHandler(sessions);
    }

    /// <summary>Reads one session back.</summary>
    /// <param name="sessionId">The session.</param>
    /// <returns>The stored session.</returns>
    private async Task<Session> StoredAsync(Guid sessionId)
    {
        return await _fixture.Context.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
    }

    /// <summary>The view-as session a reseller holds on the account ends, with the transfer as its reason.</summary>
    [Fact]
    public async Task The_view_as_session_a_reseller_holds_on_the_account_ends_with_the_transfer_as_its_reason()
    {
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var (_, login, operatorSession) = await _fixture.SeedResellerOperatorAsync();
        var viewing = await _fixture.AddViewAsSessionAsync(login, operatorSession, owner, accountId);

        await NewHandler().HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);

        var stored = await StoredAsync(viewing.Id);
        Assert.NotNull(stored.Revocation);
        Assert.Equal(SessionRevocationReason.AccountTransferred, stored.Revocation?.Reason);
        Assert.False(stored.IsActive(ViewAsFixture.Now));
    }

    /// <summary>An administrator's view-as session on the account survives, because an administrator still sees the account.</summary>
    [Fact]
    public async Task An_administrators_view_as_session_on_the_account_survives()
    {
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var viewing = await _fixture.AddViewAsSessionAsync(admin, adminSession, owner, accountId);

        await NewHandler().HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);

        Assert.Null((await StoredAsync(viewing.Id)).Revocation);
    }

    /// <summary>The customer's own session on the account survives, because the move changes nothing it can do.</summary>
    [Fact]
    public async Task The_customers_own_session_on_the_account_survives()
    {
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var own = new Session(
            Guid.NewGuid(), owner.Id, Guid.NewGuid(), Guid.NewGuid().ToString("N"), ViewAsFixture.Now,
            ViewAsFixture.Now.AddDays(14), "198.51.100.3", "browser", accountId);
        _fixture.Context.Sessions.Add(own);
        await _fixture.Context.SaveChangesAsync();

        await NewHandler().HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);

        Assert.Null((await StoredAsync(own.Id)).Revocation);
    }

    /// <summary>A reseller's view-as session on a different account survives.</summary>
    [Fact]
    public async Task A_resellers_view_as_session_on_a_different_account_survives()
    {
        var (accountId, _) = await _fixture.SeedAccountAsync("acme");
        var (otherAccountId, otherOwner) = await _fixture.SeedAccountAsync("globex");
        var (_, login, operatorSession) = await _fixture.SeedResellerOperatorAsync();
        var viewing = await _fixture.AddViewAsSessionAsync(login, operatorSession, otherOwner, otherAccountId);

        await NewHandler().HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);

        Assert.Null((await StoredAsync(viewing.Id)).Revocation);
    }

    /// <summary>The operator's own session is not a view-as session on the account and survives.</summary>
    [Fact]
    public async Task The_operators_own_session_survives()
    {
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var (_, login, operatorSession) = await _fixture.SeedResellerOperatorAsync();
        await _fixture.AddViewAsSessionAsync(login, operatorSession, owner, accountId);

        await NewHandler().HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);

        Assert.Null((await StoredAsync(operatorSession.Id)).Revocation);
    }

    /// <summary>Announcing a move for an account nobody views is harmless and repeating it changes nothing.</summary>
    [Fact]
    public async Task Announcing_a_move_for_an_account_nobody_views_is_harmless_and_repeatable()
    {
        var (accountId, owner) = await _fixture.SeedAccountAsync();
        var (_, login, operatorSession) = await _fixture.SeedResellerOperatorAsync();
        var viewing = await _fixture.AddViewAsSessionAsync(login, operatorSession, owner, accountId);
        var handler = NewHandler();

        await handler.HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);
        var firstRevokedAt = (await StoredAsync(viewing.Id)).Revocation?.At;
        await handler.HandleAsync(new AccountOwnerChanged(accountId), CancellationToken.None);
        await handler.HandleAsync(new AccountOwnerChanged(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(firstRevokedAt, (await StoredAsync(viewing.Id)).Revocation?.At);
    }
}
