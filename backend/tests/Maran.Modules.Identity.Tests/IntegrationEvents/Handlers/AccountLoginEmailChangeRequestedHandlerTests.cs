using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
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

    /// <summary>Keeps what the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

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
        var user = User.Invite(Guid.NewGuid(), username, email, Now);
        _context.Users.Add(user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, accountId, Now));
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Builds the handler over the test context.</summary>
    private AccountLoginEmailChangeRequestedHandler NewHandler()
    {
        return new AccountLoginEmailChangeRequestedHandler(
            _context,
            new AccountOwnerFinder(_context),
            new CredentialTokenRetirement(_context),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new FakeClock(Now));
    }

    /// <summary>The account's login moves to the new address.</summary>
    [Fact]
    public async Task The_accounts_login_moves_to_the_new_address()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);

        var result = await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "new@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal(UserLifecycle.Invited, stored.Lifecycle);
    }

    /// <summary>An address another login already holds is refused and the login keeps its address.</summary>
    [Fact]
    public async Task An_address_another_login_already_holds_is_refused_and_the_login_keeps_its_address()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);
        await SeedLoginAsync("owner-two", "taken@example.com", Guid.NewGuid());

        var result = await NewHandler().HandleAsync(
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

        var result = await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "same@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>An account with no login is a success with nothing to move.</summary>
    [Fact]
    public async Task An_account_with_no_login_is_a_success_with_nothing_to_move()
    {
        var result = await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "new@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>Stores an invitation token and a reset token of the user, issued at <paramref name="at"/>.</summary>
    /// <param name="userId">The login they belong to.</param>
    /// <param name="at">The instant of issue.</param>
    private async Task<(InvitationToken Invitation, PasswordResetToken Reset)> SeedTokensAsync(
        Guid userId,
        DateTimeOffset at)
    {
        var invitation = new InvitationToken(Guid.NewGuid(), userId, Guid.NewGuid().ToString("N"), at);
        var reset = new PasswordResetToken(Guid.NewGuid(), userId, Guid.NewGuid().ToString("N"), at);
        _context.InvitationTokens.Add(invitation);
        _context.PasswordResetTokens.Add(reset);
        await _context.SaveChangesAsync();
        return (invitation, reset);
    }

    /// <summary>The invitation and reset tokens mailed to the old address are refused after the move, and it is audited.</summary>
    [Fact]
    public async Task Tokens_mailed_to_the_old_address_are_refused_after_the_move()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);
        var (invitation, reset) = await SeedTokensAsync(user.Id, Now);
        Assert.True(invitation.IsUsable(Now));

        var result = await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "new@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var storedInvitation = await _context.InvitationTokens.AsNoTracking().SingleAsync(t => t.Id == invitation.Id);
        var storedReset = await _context.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == reset.Id);
        Assert.False(storedInvitation.IsUsable(Now));
        Assert.False(storedReset.IsUsable(Now));
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.CredentialTokensRetired, entry.Action);
        Assert.Equal($"{user.Id}: 2", entry.Subject);
        Assert.DoesNotContain("old@example.com", entry.Subject);
    }

    /// <summary>A token issued after the move, for the new address, works.</summary>
    [Fact]
    public async Task A_token_issued_after_the_move_stays_usable()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);
        await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "new@example.com"), CancellationToken.None);

        var (invitation, reset) = await SeedTokensAsync(user.Id, Now.AddMinutes(1));

        Assert.True(invitation.IsUsable(Now.AddMinutes(2)));
        Assert.True(reset.IsUsable(Now.AddMinutes(2)));
    }

    /// <summary>A refused move (address taken) and a no-op move retire nothing.</summary>
    [Fact]
    public async Task A_refused_or_unchanged_address_retires_no_token()
    {
        var user = await SeedLoginAsync("owner-one", "old@example.com", AccountId);
        await SeedLoginAsync("owner-two", "taken@example.com", Guid.NewGuid());
        var (invitation, _) = await SeedTokensAsync(user.Id, Now);

        await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "taken@example.com"), CancellationToken.None);
        await NewHandler().HandleAsync(
            new AccountLoginEmailChangeRequested(AccountId, "old@example.com"), CancellationToken.None);

        var stored = await _context.InvitationTokens.AsNoTracking().SingleAsync(t => t.Id == invitation.Id);
        Assert.True(stored.IsUsable(Now));
        Assert.Empty(_audit.Written);
    }
}
