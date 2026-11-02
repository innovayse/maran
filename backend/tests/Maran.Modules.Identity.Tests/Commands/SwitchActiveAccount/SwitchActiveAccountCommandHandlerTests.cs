using Maran.Modules.Identity.Commands.SwitchActiveAccount;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
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

    private IAccessTokenIssuer NewIssuer()
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
        Assert.Equal(AuditActions.ActiveAccountSwitched, entry.Action);
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
        Assert.Equal(AuditActions.ActiveAccountSwitched, entry.Action);
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

    /// <summary>A suspended login holding a live session cannot obtain a fresh token by switching.</summary>
    [Fact]
    public async Task A_suspended_login_cannot_obtain_a_token_by_switching()
    {
        _user.Suspend();
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(_second), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidCredentialsUnauthorized", result.Error!.Code);
        Assert.Equal(_first, _context.Sessions.Single(candidate => candidate.Id == _sessionId).ActiveAccountId);
    }
}
