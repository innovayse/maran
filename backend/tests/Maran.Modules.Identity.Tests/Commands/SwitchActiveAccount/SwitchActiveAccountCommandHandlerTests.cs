using Maran.Modules.Identity.Commands.SwitchActiveAccount;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.SwitchActiveAccount;

/// <summary>Behavioural contract of switching the account a sign-in speaks for.</summary>
public sealed class SwitchActiveAccountCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly RecordingAuditWriter _audit = new();
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();
    private readonly Guid _someoneElses = Guid.NewGuid();
    private User _user = null!;
    private Guid _sessionId;

    /// <summary>Seeds a customer owning two accounts, signed in on the first.</summary>
    public async Task InitializeAsync()
    {
        _user = new User(Guid.NewGuid(), "customer", "customer@example.com", "hash", UserRole.Customer, Now);
        _context.Users.Add(_user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), _user.Id, _first, Now.AddDays(-1)));
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), _user.Id, _second, Now));

        // Somebody else owns the third account; the customer holds no membership of it.
        var stranger = new User(Guid.NewGuid(), "stranger", "stranger@example.com", "hash", UserRole.Customer, Now);
        _context.Users.Add(stranger);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), stranger.Id, _someoneElses, Now));
        await _context.SaveChangesAsync();

        var issued = await NewSessions().IssueAsync(_user.Id, "203.0.113.7", "agent", _first, CancellationToken.None);
        _sessionId = issued.SessionId;
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private SessionService NewSessions()
    {
        return new SessionService(
            _context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
    }

    private JwtAccessTokenIssuer NewIssuer()
    {
        var options = new OptionsWrapper<JwtOptions>(new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            AccessTokenMinutes = 15,
        });

        return new JwtAccessTokenIssuer(options, TestSecurityPolicyCache.Over(_context), _clock);
    }

    private SwitchActiveAccountCommandHandler NewHandler()
    {
        return new SwitchActiveAccountCommandHandler(
            _context,
            NewIssuer(),
            _clock,
            new IdentityAuditJournal(_audit, new StubCurrentUser(_user.Id, "customer")));
    }

    private SwitchActiveAccountCommand Command(Guid accountId)
    {
        return new SwitchActiveAccountCommand(accountId, _user.Id, _sessionId, "203.0.113.7", "agent");
    }

    /// <summary>Switching to a second owned account moves the session, signs a token for it, and is audited.</summary>
    [Fact]
    public async Task Switching_to_a_second_owned_account_moves_the_session_and_is_audited()
    {
        var result = await NewHandler().HandleAsync(Command(_second), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_second, result.Value.AccountId);
        var session = _context.Sessions.Single(candidate => candidate.Id == _sessionId);
        Assert.Equal(_second, session.ActiveAccountId);
        var claims = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(result.Value.AccessToken);
        Assert.Equal(_second.ToString(), claims.GetClaim(PanelClaimTypes.AccountId).Value);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ActiveAccountSwitched, entry.Action);
        Assert.Equal(_second.ToString(), entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An account without a membership answers not found, changes nothing, and is journalled.</summary>
    [Fact]
    public async Task An_account_without_a_membership_answers_not_found_and_changes_nothing()
    {
        var result = await NewHandler().HandleAsync(Command(_someoneElses), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountMembershipNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(_first, _context.Sessions.Single(candidate => candidate.Id == _sessionId).ActiveAccountId);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ActiveAccountSwitched, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An account that does not exist answers exactly as one that belongs to somebody else.</summary>
    [Fact]
    public async Task A_nonexistent_account_answers_exactly_like_somebody_elses()
    {
        var unknown = await NewHandler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);
        var foreign = await NewHandler().HandleAsync(Command(_someoneElses), CancellationToken.None);

        Assert.Equal(foreign.Error!.Code, unknown.Error!.Code);
        Assert.Equal(foreign.Error.Type, unknown.Error.Type);
    }

    /// <summary>A session belonging to somebody else cannot be switched.</summary>
    [Fact]
    public async Task Another_logins_session_cannot_be_switched()
    {
        var other = await NewSessions()
            .IssueAsync(Guid.NewGuid(), "198.51.100.4", "other", null, CancellationToken.None);

        var result = await NewHandler().HandleAsync(
            Command(_second) with { SessionId = other.SessionId },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SessionNotFound", result.Error!.Code);
    }

    /// <summary>A login that may not be used holding a live session cannot obtain a fresh token by switching.</summary>
    [Fact]
    public async Task A_login_that_may_not_be_used_cannot_obtain_a_token_by_switching()
    {
        var invited = User.Invite(Guid.NewGuid(), "invited", "invited@example.com", Now);
        var account = Guid.NewGuid();
        _context.Users.Add(invited);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), invited.Id, account, Now));
        await _context.SaveChangesAsync();
        var issued = await NewSessions().IssueAsync(invited.Id, "203.0.113.9", "agent", account, CancellationToken.None);

        var result = await NewHandler().HandleAsync(
            new SwitchActiveAccountCommand(account, invited.Id, issued.SessionId, "203.0.113.9", "agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidCredentialsUnauthorized", result.Error!.Code);
    }

    /// <summary>Suspends the caller's membership of <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account whose membership is suspended.</param>
    private async Task SuspendAsync(Guid accountId)
    {
        var membership = await _context.AccountMemberships.SingleAsync(
            candidate => candidate.UserId == _user.Id && candidate.AccountId == accountId);
        membership.Suspend();
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Switching to an account the caller holds but whose membership is suspended is refused with
    /// its own code, changes nothing, and is journalled as a failure.
    /// </summary>
    [Fact]
    public async Task Switching_to_a_suspended_membership_is_refused_changes_nothing_and_is_audited()
    {
        await SuspendAsync(_second);

        var result = await NewHandler().HandleAsync(Command(_second), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountMembershipSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(_first, _context.Sessions.Single(candidate => candidate.Id == _sessionId).ActiveAccountId);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ActiveAccountSwitched, entry.Action);
        Assert.Equal(_second.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>
    /// Suspending the account the session speaks for does not stop a switch to the other one: the
    /// suspension belongs to one account, not to the login.
    /// </summary>
    [Fact]
    public async Task Switching_away_from_a_suspended_account_to_an_active_one_succeeds()
    {
        await SuspendAsync(_first);

        var result = await NewHandler().HandleAsync(Command(_second), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_second, result.Value.AccountId);
    }

    /// <summary>A suspended membership answers a stranger's account id exactly as before: not found.</summary>
    [Fact]
    public async Task A_suspended_membership_does_not_change_the_answer_for_somebody_elses_account()
    {
        await SuspendAsync(_second);

        var result = await NewHandler().HandleAsync(Command(_someoneElses), CancellationToken.None);

        Assert.Equal("AccountMembershipNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
