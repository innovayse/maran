using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioral contract of <see cref="AccountLoginEmailChangeRequestedHandler"/>: the account's login
/// follows a new owner address unless another login already holds it, and an account with no login
/// is a success with nothing to move.
/// </summary>
public sealed class AccountLoginEmailChangeRequestedHandlerTests : IDisposable
{
    /// <summary>The hosting account whose login moves.</summary>
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The fixed instant logins are created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Stores a login with <paramref name="email"/>, owning <paramref name="accountId"/>.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="email">The login's address.</param>
    /// <param name="accountId">The account it owns.</param>
    private async Task<User> SeedLoginAsync(string username, string email, Guid accountId)
    {
        var user = User.Invite(Guid.NewGuid(), username, email, accountId, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>The account's login moves to the new address.</summary>
    [Fact]
    public async Task The_accounts_login_moves_to_the_new_address()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);

        var result = await new AccountLoginEmailChangeRequestedHandler(_context).HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "new@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal(UserState.Invited, stored.State);
    }

    /// <summary>An address another login already holds is refused and the login keeps its address.</summary>
    [Fact]
    public async Task An_address_another_login_already_holds_is_refused_and_the_login_keeps_its_address()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);
        await SeedLoginAsync("owner-two", "taken@example.com", Guid.NewGuid());

        var result = await new AccountLoginEmailChangeRequestedHandler(_context).HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "taken@example.com"), CancellationToken.None);

        Assert.Equal("EmailTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("old@example.com", stored.Email);
    }

    /// <summary>Keeping the address the login already has is accepted.</summary>
    [Fact]
    public async Task Keeping_the_address_the_login_already_has_is_accepted()
    {
        await SeedLoginAsync("owner-one", "same@example.com", AccountId);

        var result = await new AccountLoginEmailChangeRequestedHandler(_context).HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "same@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>An account with no login is a success with nothing to move.</summary>
    [Fact]
    public async Task An_account_with_no_login_is_a_success_with_nothing_to_move()
    {
        var result = await new AccountLoginEmailChangeRequestedHandler(_context).HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "new@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _context.Users.ToListAsync());
    }
}
