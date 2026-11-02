using Maran.Modules.Identity.Commands.RefreshSession;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.RefreshSession;

/// <summary>Behavioural contract of the refresh handler, on the question of WHO may refresh.</summary>
/// <remarks>
/// <b>Central to this file:</b> a refresh mints a working access token from a credential that was
/// verified in the past, so it is a way in, and it asked nothing about the login's state — only
/// whether the user still existed (issue #58). Suspension is applied as two separate operations in
/// <c>AccountSuspendingHandler</c> (<c>Suspend()</c> and <c>SaveChangesAsync</c>, then
/// <c>RevokeAllAsync</c>), so anything that prevents the second leaves a suspended login holding live
/// sessions — and each refresh would rotate one into a fresh token indefinitely. These tests seed
/// exactly that state: suspended, with its session deliberately NOT revoked.
/// </remarks>
public sealed class RefreshSessionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly RecordingAuditWriter _audit = new();
    private readonly CountingAccessTokenIssuer _issuer = new();

    /// <summary>Records whether a token was minted at all, which is the claim these tests need.</summary>
    /// <remarks>
    /// Asserting on the returned <c>Result</c> alone would pass for a handler that signed a token and
    /// then discarded it. The refusal has to happen BEFORE anything is signed, so the double counts.
    /// </remarks>
    private sealed class CountingAccessTokenIssuer : IAccessTokenIssuer
    {
        /// <summary>How many times a token was asked for.</summary>
        public int Issued { get; private set; }

        /// <summary>The account the most recent token was issued for, or null when it spoke for none.</summary>
        public Guid? LastAccountId { get; private set; }

        /// <summary>Counts the call and returns a token whose value is never inspected.</summary>
        /// <param name="user">Ignored.</param>
        /// <param name="sessionId">Ignored.</param>
        /// <param name="accountId">Remembered, so a test can assert which account the token spoke for.</param>
        /// <param name="cancellationToken">Ignored.</param>
        /// <returns>A token that exists only so the handler can continue.</returns>
        public Task<AccessToken> IssueAsync(
            User user,
            Guid sessionId,
            Guid? accountId,
            CancellationToken cancellationToken)
        {
            Issued++;
            LastAccountId = accountId;
            return Task.FromResult(new AccessToken("signed", Now.AddMinutes(15), false));
        }

        /// <summary>Counts the call; a refresh must never reach it.</summary>
        /// <param name="viewedUser">Ignored.</param>
        /// <param name="session">Ignored.</param>
        /// <param name="actor">Ignored.</param>
        /// <returns>A token that exists only so a caller could continue.</returns>
        public AccessToken IssueViewAs(User viewedUser, Session session, User actor)
        {
            Issued++;
            return new AccessToken("signed", Now.AddMinutes(15), false);
        }
    }

    private SessionService NewSessions()
    {
        return new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
    }

    private RefreshSessionCommandHandler NewHandler(SessionService sessions)
    {
        return new RefreshSessionCommandHandler(
            _context,
            sessions,
            _issuer,
            new ActiveAccountResolver(_context),
            new IdentityAuditJournal(_audit, new StubCurrentUser()));
    }

    /// <summary>
    /// Seeds a login in <paramref name="state"/> that holds a live session, and returns the refresh
    /// token for it. The session is issued BEFORE the state changes, which is the real sequence: the
    /// session was legitimate when it was created.
    /// </summary>
    private async Task<string> SeedLoginHoldingALiveSessionAsync(UserState state)
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var issued = await NewSessions().IssueAsync(user.Id, "203.0.113.7", "agent", null, CancellationToken.None);

        // The suspension's first half only. Its second half — RevokeAllAsync — is deliberately not
        // run: this is the state the panel is left in when anything stops it, and it is the state the
        // handler used to accept.
        if (state == UserState.Suspended)
        {
            user.Suspend();
        }

        await _context.SaveChangesAsync();
        return issued.RefreshToken;
    }

    /// <summary>
    /// A suspended login holding a live session is refused, and no token is signed for it. This went
    /// red before the state check existed in the refresh path.
    /// </summary>
    [Fact]
    public async Task A_suspended_login_holding_a_live_session_cannot_refresh()
    {
        var token = await SeedLoginHoldingALiveSessionAsync(UserState.Suspended);

        var result = await NewHandler(NewSessions()).HandleAsync(
            new RefreshSessionCommand(token, "203.0.113.7", "agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RefreshTokenInvalidUnauthorized", result.Error!.Code);
        Assert.Equal(0, _issuer.Issued);
    }

    /// <summary>
    /// The inverse control, without which the test above would pass for a handler that refuses every
    /// refresh: an active login holding the same kind of session refreshes normally and gets a token.
    /// </summary>
    [Fact]
    public async Task An_active_login_still_refreshes_and_is_issued_a_token()
    {
        var token = await SeedLoginHoldingALiveSessionAsync(UserState.Active);

        var result = await NewHandler(NewSessions()).HandleAsync(
            new RefreshSessionCommand(token, "203.0.113.7", "agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _issuer.Issued);
    }

    /// <summary>A refresh keeps acting for the account the session was switched to, not the first one.</summary>
    [Fact]
    public async Task A_refresh_keeps_the_account_the_session_was_switched_to()
    {
        var (user, first, second) = await SeedLoginOwningTwoAccountsAsync();
        var issued = await NewSessions().IssueAsync(user.Id, "203.0.113.7", "agent", second, CancellationToken.None);

        var result = await NewHandler(NewSessions()).HandleAsync(
            new RefreshSessionCommand(issued.RefreshToken, "203.0.113.7", "agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(second, _issuer.LastAccountId);
        Assert.Equal(second, result.Value.Session.ActiveAccountId);
        Assert.NotEqual(first, _issuer.LastAccountId);
    }

    /// <summary>
    /// A refresh stops acting for an account whose membership has gone and falls back to the oldest
    /// one the login still holds, storing the correction on the session.
    /// </summary>
    [Fact]
    public async Task A_refresh_stops_acting_for_an_account_the_login_no_longer_holds()
    {
        var (user, first, second) = await SeedLoginOwningTwoAccountsAsync();
        var issued = await NewSessions().IssueAsync(user.Id, "203.0.113.7", "agent", second, CancellationToken.None);
        _context.AccountMemberships.RemoveRange(_context.AccountMemberships.Where(m => m.AccountId == second));
        await _context.SaveChangesAsync();

        var result = await NewHandler(NewSessions()).HandleAsync(
            new RefreshSessionCommand(issued.RefreshToken, "203.0.113.7", "agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(first, _issuer.LastAccountId);
        var stored = _context.Sessions.Single(candidate => candidate.Id == result.Value.Session.SessionId);
        Assert.Equal(first, stored.ActiveAccountId);
    }

    /// <summary>Seeds an active customer who owns two accounts, the first created a day before the second.</summary>
    /// <returns>The login and the ids of its older and newer accounts.</returns>
    private async Task<(User User, Guid First, Guid Second)> SeedLoginOwningTwoAccountsAsync()
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _context.Users.Add(user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, first, Now.AddDays(-1)));
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, second, Now));
        await _context.SaveChangesAsync();

        return (user, first, second);
    }
}
