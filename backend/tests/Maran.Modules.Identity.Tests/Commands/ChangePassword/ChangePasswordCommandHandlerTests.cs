using Maran.Modules.Identity.Commands.ChangePassword;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.ChangePassword;

/// <summary>Behavioural contract of the signed-in password change.</summary>
public sealed class ChangePasswordCommandHandlerTests : IAsyncLifetime
{
    private const string OldPassword = "the old password";

    private const string NewPassword = "correct horse battery staple";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly Argon2idPasswordHasher _hasher = new();
    private readonly FakeClock _clock = new(Now);
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>Seeds the user every test changes the password of.</summary>
    public async Task InitializeAsync()
    {
        _context.Users.Add(new User(
            _userId, "admin", "admin@example.com", _hasher.Hash(OldPassword), UserRole.Admin, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private SessionService NewSessions()
    {
        return new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 }));
    }

    private ChangePasswordCommandHandler NewHandler()
    {
        return new ChangePasswordCommandHandler(
            _context,
            _hasher,
            NewSessions(),
            TestSecurityPolicyCache.Over(_context),
            new IdentityAuditJournal(_audit, new StubCurrentUser(_userId, "admin")),
            _clock);
    }

    private ChangePasswordCommand Command(string currentPassword, Guid currentSessionId = default)
    {
        return new ChangePasswordCommand(currentPassword, NewPassword, _userId, currentSessionId, "203.0.113.7", "agent");
    }

    private async Task<User> ReloadUserAsync()
    {
        return await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == _userId);
    }

    /// <summary>The right current password sets the new one.</summary>
    [Fact]
    public async Task The_right_current_password_sets_the_new_one()
    {
        var result = await NewHandler().HandleAsync(Command(OldPassword), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = await ReloadUserAsync();
        Assert.True(_hasher.Verify(NewPassword, user.PasswordHash));
        Assert.False(_hasher.Verify(OldPassword, user.PasswordHash));
    }

    /// <summary>A wrong current password is refused with its code and changes nothing.</summary>
    [Fact]
    public async Task A_wrong_current_password_is_refused_with_its_code_and_changes_nothing()
    {
        var result = await NewHandler().HandleAsync(Command("not the password"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("CurrentPasswordIncorrect", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.True(_hasher.Verify(OldPassword, (await ReloadUserAsync()).PasswordHash));
    }

    /// <summary>A wrong current password counts against the account as a failed sign-in does.</summary>
    [Fact]
    public async Task A_wrong_current_password_counts_against_the_account_as_a_failed_sign_in_does()
    {
        await NewHandler().HandleAsync(Command("not the password"), CancellationToken.None);

        Assert.Equal(1, (await ReloadUserAsync()).FailedLoginAttempts);
    }

    /// <summary>Enough wrong guesses lock the account, and then even the right password is refused.</summary>
    [Fact]
    public async Task Enough_wrong_guesses_lock_the_account_and_then_even_the_right_password_is_refused()
    {
        var policy = SecurityPolicySnapshot.Default;
        for (var attempt = 0; attempt < policy.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Command("not the password"), CancellationToken.None);
        }

        Assert.True((await ReloadUserAsync()).IsLockedOut(Now));

        var result = await NewHandler().HandleAsync(Command(OldPassword), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("CurrentPasswordIncorrect", result.Error!.Code);
        Assert.True(_hasher.Verify(OldPassword, (await ReloadUserAsync()).PasswordHash));
    }

    /// <summary>A successful change clears earlier failed attempts.</summary>
    [Fact]
    public async Task A_successful_change_clears_earlier_failed_attempts()
    {
        await NewHandler().HandleAsync(Command("not the password"), CancellationToken.None);

        await NewHandler().HandleAsync(Command(OldPassword), CancellationToken.None);

        Assert.Equal(0, (await ReloadUserAsync()).FailedLoginAttempts);
    }

    /// <summary>Every other session is revoked and the caller's own is kept.</summary>
    /// <remarks>
    /// The protection this pins: a password is changed because it may be in somebody else's hands,
    /// and their refresh cookie outlives the change unless something ends it. Removing the revoke
    /// call, or revoking the caller's own session too, must turn this test red.
    /// </remarks>
    [Fact]
    public async Task Every_other_session_is_revoked_and_the_callers_own_is_kept()
    {
        var thief = await NewSessions().IssueAsync(_userId, "198.51.100.4", "thief", CancellationToken.None);
        var owner = await NewSessions().IssueAsync(_userId, "203.0.113.7", "owner", CancellationToken.None);

        await NewHandler().HandleAsync(Command(OldPassword, owner.SessionId), CancellationToken.None);

        var sessions = await _context.Sessions.AsNoTracking().ToListAsync();
        var revoked = sessions.Single(session =>
        {
            return session.Id == thief.SessionId;
        });
        var kept = sessions.Single(session =>
        {
            return session.Id == owner.SessionId;
        });
        Assert.Equal(SessionRevocationReason.PasswordChanged, revoked.RevocationReason);
        Assert.Null(kept.RevokedAt);
    }

    /// <summary>The caller's whole rotation family is kept, so a refresh in another tab survives.</summary>
    [Fact]
    public async Task The_callers_whole_rotation_family_is_kept_so_a_refresh_in_another_tab_survives()
    {
        var owner = await NewSessions().IssueAsync(_userId, "203.0.113.7", "owner", CancellationToken.None);
        var rotated = await NewSessions().RotateAsync(owner.RefreshToken, "203.0.113.7", "owner", CancellationToken.None);

        await NewHandler().HandleAsync(Command(OldPassword, owner.SessionId), CancellationToken.None);

        var live = await _context.Sessions.AsNoTracking().SingleAsync(session => session.Id == rotated.Value.SessionId);
        Assert.Null(live.RevokedAt);
    }

    /// <summary>A refused change revokes no session.</summary>
    [Fact]
    public async Task A_refused_change_revokes_no_session()
    {
        await NewSessions().IssueAsync(_userId, "198.51.100.4", "other", CancellationToken.None);

        await NewHandler().HandleAsync(Command("not the password"), CancellationToken.None);

        Assert.All(await _context.Sessions.AsNoTracking().ToListAsync(), session =>
        {
            Assert.Null(session.RevokedAt);
        });
    }

    /// <summary>A change is journalled and neither password reaches the entry.</summary>
    [Fact]
    public async Task A_change_is_journalled_and_neither_password_reaches_the_entry()
    {
        await NewHandler().HandleAsync(Command(OldPassword), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.PasswordChanged, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal(_userId.ToString(), entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        foreach (var secret in new[] { OldPassword, NewPassword })
        {
            Assert.DoesNotContain(secret, entry.Subject, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.ActorUsername, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.UserAgent, StringComparison.Ordinal);
        }
    }

    /// <summary>A refused change is journalled as a failed password change.</summary>
    [Fact]
    public async Task A_refused_change_is_journalled_as_a_failed_password_change()
    {
        await NewHandler().HandleAsync(Command("not the password"), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.PasswordChanged, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.DoesNotContain("not the password", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A token naming a user that no longer exists is not found.</summary>
    [Fact]
    public async Task A_token_naming_a_user_that_no_longer_exists_is_not_found()
    {
        var command = Command(OldPassword) with { UserId = Guid.NewGuid() };

        var result = await NewHandler().HandleAsync(command, CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
