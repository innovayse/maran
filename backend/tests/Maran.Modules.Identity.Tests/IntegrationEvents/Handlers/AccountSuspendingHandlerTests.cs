using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that blocks a login and revokes its sessions when the
/// account it owns is suspended.
/// </summary>
public sealed class AccountSuspendingHandlerTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static AccountSuspending Suspending()
    {
        return new AccountSuspending(AccountId, "owner-one");
    }

    private AccountSuspendingHandler NewHandler()
    {
        return new AccountSuspendingHandler(_context, NewSessions());
    }

    private SessionService NewSessions()
    {
        return new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
    }

    private async Task<User> SeedActiveUserAsync()
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        user.AssignAccount(AccountId);
        user.Activate("hash");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Suspension revokes the live sessions of an already-signed-in owner, not merely the next
    /// sign-in.
    /// </summary>
    /// <remarks>
    /// This is the property the type's own remarks name as the half of a suspension that actually
    /// matters: blocking <see cref="User.State"/> alone leaves a session issued before the
    /// suspension authenticating for the rest of its own lifetime. Removing the
    /// <c>RevokeAllAsync</c> call from the handler must turn this test red — asserting only that the
    /// LOGIN is blocked would not catch that regression, because the login and the session are two
    /// separate rows.
    /// </remarks>
    [Fact]
    public async Task Suspension_revokes_the_live_sessions_of_an_already_signed_in_owner()
    {
        var user = await SeedActiveUserAsync();
        var issued = await NewSessions().IssueAsync(user.Id, "203.0.113.7", "browser", CancellationToken.None);

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var session = await _context.Sessions.SingleAsync(candidate => candidate.Id == issued.SessionId);
        Assert.False(session.IsActive(Now));
        Assert.NotNull(session.RevokedAt);
        Assert.Equal(SessionRevocationReason.AccountSuspended, session.RevocationReason);
    }

    /// <summary>Suspending an account blocks its login from signing in again.</summary>
    [Fact]
    public async Task Suspending_an_account_blocks_its_login_from_signing_in_again()
    {
        await SeedActiveUserAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var user = await _context.Users.SingleAsync(candidate => candidate.AccountId == AccountId);
        Assert.Equal(UserState.Suspended, user.State);
    }

    /// <summary>Suspension leaves an invited login invited, so the unused invitation survives.</summary>
    /// <remarks>
    /// An owner who never accepted their invitation has no password and no session to revoke; a
    /// handler that moved such a login to <c>Suspended</c> anyway would silently consume an
    /// invitation nobody has used yet, and resuming the account later would leave a login neither
    /// invited nor usable without a separate repair.
    /// </remarks>
    [Fact]
    public async Task Suspension_leaves_an_invited_login_invited()
    {
        var user = User.Invite(Guid.NewGuid(), "owner-one", "owner@example.com", AccountId, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.AccountId == AccountId);
        Assert.Equal(UserState.Invited, stored.State);
    }

    /// <summary>An account with no login at all is left with nothing to do.</summary>
    /// <remarks>
    /// The ordinary case for every account but the administrator's, whose <c>AccountId</c> is null
    /// and is therefore never named by this event.
    /// </remarks>
    [Fact]
    public async Task An_account_with_no_login_at_all_is_left_with_nothing_to_do()
    {
        await NewHandler().HandleAsync(Suspending(), CancellationToken.None);

        Assert.Empty(await _context.Users.ToListAsync());
        Assert.Empty(await _context.Sessions.ToListAsync());
    }
}
