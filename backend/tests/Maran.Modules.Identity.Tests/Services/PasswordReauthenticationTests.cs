using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the password re-entry window the marketplace asks before a privileged
/// action: the right password passes, every kind of failure is the one coded refusal, and a wrong
/// password counts against the same lockout as a wrong sign-in.
/// </summary>
public sealed class PasswordReauthenticationTests : IAsyncLifetime
{
    /// <summary>The seeded administrator's password.</summary>
    private const string Password = "the administrator password";

    /// <summary>A password that is not the seeded one.</summary>
    private const string WrongPassword = "not the password";

    /// <summary>The instant every test runs at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's database.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The audit writer the journal writes to.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The real hasher, so the right password really verifies.</summary>
    private readonly Argon2idPasswordHasher _hasher = new();

    /// <summary>The panel's clock, fixed.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>The seeded administrator.</summary>
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>Seeds the administrator every test re-authenticates.</summary>
    /// <returns>Resolves once the user is stored.</returns>
    public async Task InitializeAsync()
    {
        _context.Users.Add(new User(
            _userId, "admin", "admin@example.com", _hasher.Hash(Password), UserRole.Admin, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated.</summary>
    /// <returns>Resolves once the database is released.</returns>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    /// <summary>Builds the service over the shared database, as the container would.</summary>
    /// <returns>The service.</returns>
    private PasswordReauthentication NewService()
    {
        return new PasswordReauthentication(
            _context,
            _hasher,
            new UnlockedFailedLoginRecorder(_context, TestSecurityPolicyCache.Over(_context)),
            new IdentityAuditJournal(_audit, new StubCurrentUser(_userId, "admin")),
            _clock);
    }

    /// <summary>Reads the administrator back from the database.</summary>
    /// <returns>The stored user.</returns>
    private async Task<User> ReloadUserAsync()
    {
        return await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == _userId);
    }

    /// <summary>The right password succeeds.</summary>
    [Fact]
    public async Task The_right_password_succeeds()
    {
        var result = await NewService().VerifyAsync(_userId, Password, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_audit.Written);
    }

    /// <summary>A wrong password fails with its code, as a validation failure and never an unauthorized one.</summary>
    [Fact]
    public async Task A_wrong_password_fails_with_its_code_as_a_validation_failure()
    {
        var result = await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ReauthenticationFailed", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>A wrong password counts against the login as a failed sign-in does.</summary>
    [Fact]
    public async Task A_wrong_password_counts_against_the_login_as_a_failed_sign_in_does()
    {
        await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);

        Assert.Equal(1, (await ReloadUserAsync()).FailedLoginAttempts);
    }

    /// <summary>Enough wrong passwords lock the login, and then even the right password is refused.</summary>
    /// <remarks>
    /// The positive control is the first call: before the lock, the right password worked. Without it a
    /// service that refused everything would pass.
    /// </remarks>
    [Fact]
    public async Task Enough_wrong_passwords_lock_the_login_and_then_even_the_right_password_is_refused()
    {
        Assert.True((await NewService().VerifyAsync(_userId, Password, CancellationToken.None)).IsSuccess);

        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
        {
            await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);
        }

        Assert.True((await ReloadUserAsync()).IsLockedOut(Now));

        var result = await NewService().VerifyAsync(_userId, Password, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ReauthenticationFailed", result.Error!.Code);
    }

    /// <summary>A locked login is refused with the very failure a wrong password gets.</summary>
    [Fact]
    public async Task A_locked_login_gets_the_same_failure_as_a_wrong_password()
    {
        var wrong = await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
        {
            await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);
        }

        var locked = await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);

        Assert.Equal(wrong.Error, locked.Error);
    }

    /// <summary>A user that does not exist fails exactly as a wrong password does.</summary>
    [Fact]
    public async Task An_unknown_user_fails_exactly_as_a_wrong_password_does()
    {
        var wrong = await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);

        var unknown = await NewService().VerifyAsync(Guid.NewGuid(), Password, CancellationToken.None);

        Assert.False(unknown.IsSuccess);
        Assert.Equal(wrong.Error, unknown.Error);
    }

    /// <summary>The right password clears earlier failed attempts, as a successful sign-in does.</summary>
    [Fact]
    public async Task The_right_password_clears_earlier_failed_attempts()
    {
        await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);

        await NewService().VerifyAsync(_userId, Password, CancellationToken.None);

        Assert.Equal(0, (await ReloadUserAsync()).FailedLoginAttempts);
    }

    /// <summary>A refused re-entry is journalled against the user and the password never reaches the entry.</summary>
    [Fact]
    public async Task A_refused_re_entry_is_journalled_and_the_password_never_reaches_the_entry()
    {
        await NewService().VerifyAsync(_userId, WrongPassword, CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ReauthenticationFailed, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(_userId, entry.ActorUserId);
        Assert.Equal(_userId.ToString(), entry.Subject);
        Assert.DoesNotContain(WrongPassword, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, entry.Subject, StringComparison.Ordinal);
    }
}
