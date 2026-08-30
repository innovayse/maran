using Maran.Modules.Identity.Commands.Login;
using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.Login;
/// <summary>Behavioural contract of login command handler.</summary>

public sealed class LoginCommandHandlerTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly Argon2idPasswordHasher _hasher = new();
    private readonly RecordingAuditWriter _audit = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static LoginCommand Attempt(string username = "admin", string password = Password)
    {
        return new LoginCommand(username, password, "203.0.113.7", "agent");
    }

    private async Task<User> SeedUserAsync(string passwordHash, bool withTotp = false)
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", passwordHash, UserRole.Admin, Now);
        if (withTotp)
        {
            user.EnableTotp("JBSWY3DPEHPK3PXP");
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private LoginCommandHandler NewHandler(IPasswordHasher? hasher = null)
    {
        var options = Options.Create(new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14,
        });

        return new LoginCommandHandler(
            _context,
            hasher ?? _hasher,
            new JwtAccessTokenIssuer(options, _clock),
            new SessionService(_context, _clock, options),
            _audit,
            _clock);
    }

    /// <summary>Logging in with the right password returns an access token.</summary>
    [Fact]
    public async Task Logging_in_with_the_right_password_returns_an_access_token()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Response.AccessToken));
        Assert.NotNull(result.Value.Session);
    }

    /// <summary>Logging in with a wrong password fails with the credentials error.</summary>
    [Fact]
    public async Task Logging_in_with_a_wrong_password_fails_with_the_credentials_error()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        var result = await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);

        Assert.Equal("InvalidCredentialsUnauthorized", result.Error!.Code);
    }

    /// <summary>Logging in as an unknown user fails with the same error as a wrong password.</summary>
    [Fact]
    public async Task Logging_in_as_an_unknown_user_fails_with_the_same_error_as_a_wrong_password()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        var wrongPassword = await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        var unknownUser = await NewHandler().HandleAsync(Attempt(username: "nosuchuser"), CancellationToken.None);

        Assert.Equal(wrongPassword.Error!.Code, unknownUser.Error!.Code);
    }

    /// <summary>A failed login issues no session at all.</summary>
    [Fact]
    public async Task A_failed_login_issues_no_session_at_all()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);

        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>A user with two factor enabled gets no access token and no session yet.</summary>
    [Fact]
    public async Task A_user_with_two_factor_enabled_gets_no_access_token_and_no_session_yet()
    {
        await SeedUserAsync(_hasher.Hash(Password), withTotp: true);

        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.True(result.Value.Response.TwoFactorRequired);
        Assert.Null(result.Value.Response.AccessToken);
        Assert.Null(result.Value.Session);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>A successful login writes an audit event and records the login instant.</summary>
    [Fact]
    public async Task A_successful_login_writes_an_audit_event_and_records_the_login_instant()
    {
        var user = await SeedUserAsync(_hasher.Hash(Password));

        await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.Equal(AuditActions.LoginSucceeded, _audit.Written.Single().Action);
        Assert.Equal(Now, (await _context.Users.SingleAsync(u => u.Id == user.Id)).LastLoginAt);
    }

    /// <summary>A failed login writes an audit event that does not contain the attempted password.</summary>
    [Fact]
    public async Task A_failed_login_writes_an_audit_event_that_does_not_contain_the_attempted_password()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        await NewHandler().HandleAsync(Attempt(password: "hunter2"), CancellationToken.None);

        var entry = _audit.Written.Single();
        Assert.Equal(AuditActions.LoginFailed, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.DoesNotContain("hunter2", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", entry.ActorUsername, StringComparison.Ordinal);
    }

    /// <summary>A login by an unknown user is audited with the name that was tried.</summary>
    [Fact]
    public async Task A_login_by_an_unknown_user_is_audited_with_the_name_that_was_tried()
    {
        await NewHandler().HandleAsync(Attempt(username: "nosuchuser"), CancellationToken.None);

        var entry = _audit.Written.Single();
        Assert.Equal(AuditActions.LoginFailed, entry.Action);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("nosuchuser", entry.ActorUsername);
    }

    /// <summary>A login by a user whose hash is stale upgrades the stored hash.</summary>
    [Fact]
    public async Task A_login_by_a_user_whose_hash_is_stale_upgrades_the_stored_hash()
    {
        var stored = _hasher.Hash(Password);
        await SeedUserAsync(stored);
        var handler = NewHandler(new StaleHashPasswordHasher(stored));

        await handler.HandleAsync(Attempt(), CancellationToken.None);

        Assert.NotEqual(stored, (await _context.Users.SingleAsync()).PasswordHash);
    }

    /// <summary>Repeated wrong passwords eventually lock the account.</summary>
    [Fact]
    public async Task Repeated_wrong_passwords_eventually_lock_the_account()
    {
        var user = await SeedUserAsync(_hasher.Hash(Password));

        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        Assert.True(user.IsLockedOut(Now));
    }

    /// <summary>A locked account is refused even when the password is right.</summary>
    [Fact]
    public async Task A_locked_account_is_refused_even_when_the_password_is_right()
    {
        await SeedUserAsync(_hasher.Hash(Password));
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    /// <summary>A locked account is refused with the same error a wrong password gets.</summary>
    [Fact]
    public async Task A_locked_account_is_refused_with_the_same_error_a_wrong_password_gets()
    {
        // A distinct "locked" code would tell an attacker the account exists and that their
        // guessing had tripped the lock. Both answers must be the one word: no.
        await SeedUserAsync(_hasher.Hash(Password));
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        var locked = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);
        var unknownUser = await NewHandler().HandleAsync(Attempt(username: "nosuchuser"), CancellationToken.None);

        Assert.Equal(unknownUser.Error!.Code, locked.Error!.Code);
    }

    /// <summary>The lock lifts once its window has passed.</summary>
    [Fact]
    public async Task The_lock_lifts_once_its_window_has_passed()
    {
        await SeedUserAsync(_hasher.Hash(Password));
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        _clock.Advance(User.LockoutDuration + TimeSpan.FromSeconds(1));
        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A successful sign-in resets the failures before they reach the threshold.</summary>
    [Fact]
    public async Task A_successful_sign_in_resets_the_failures_before_they_reach_the_threshold()
    {
        var user = await SeedUserAsync(_hasher.Hash(Password));
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts - 1; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.Equal(0, user.FailedLoginAttempts);
    }
}
