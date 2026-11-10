using Maran.Modules.Identity.Commands.VerifyTwoFactor;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OtpNet;

namespace Maran.Modules.Identity.Tests.Commands.VerifyTwoFactor;

/// <summary>Behavioural contract of the two-factor verification handler.</summary>
/// <remarks>
/// <b>Central to this file:</b> a suspended login enrolled in two-factor must be refused here
/// exactly as a wrong password is, identically to how <c>LoginCommandHandlerTests</c> proves it for
/// the first step. This handler is reachable on its own — its own remarks say so — so a review found
/// it had no state check at all: a customer whose account was suspended, and whose sessions
/// <c>AccountSuspendingHandler</c> had already revoked, could still mint a fresh one by presenting
/// their still-correct password and a still-valid TOTP code straight to this endpoint.
/// </remarks>
public sealed class VerifyTwoFactorCommandHandlerTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private const string Secret = "JBSWY3DPEHPK3PXP";

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly Argon2idPasswordHasher _hasher = new();
    private readonly RecordingAuditWriter _audit = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>What the latest monitoring round said about the clock; synchronised by default.</summary>
    private readonly StubClockSyncStatus _clockStatus = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static VerifyTwoFactorCommand Attempt(string code, string username = "owner-one", string password = Password)
    {
        return new VerifyTwoFactorCommand(username, password, code, "203.0.113.7", "agent");
    }

    private async Task<User> SeedTotpUserAsync()
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", _hasher.Hash(Password), UserRole.Customer, Now);
        user.EnableTotp(Secret);
        _context.Users.Add(user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, Guid.NewGuid(), Now));
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Suspends the only account the seeded customer holds, as the suspension cascade does.</summary>
    private async Task SuspendTheCustomersAccountAsync()
    {
        (await _context.AccountMemberships.SingleAsync()).Suspend();
        await _context.SaveChangesAsync();
    }

    private static string CurrentCode()
    {
        return new Totp(Base32Encoding.ToBytes(Secret)).ComputeTotp(Now.UtcDateTime);
    }

    private VerifyTwoFactorCommandHandler NewHandler()
    {
        var options = new OptionsWrapper<JwtOptions>(new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14,
        });

        return new VerifyTwoFactorCommandHandler(
            _context,
            _hasher,
            new TotpService(_clock),
            new RecoveryCodeService(_context, _hasher, _clock),
            new AuthenticationCompleter(
                new JwtAccessTokenIssuer(options, TestSecurityPolicyCache.Over(_context), _clock),
                new SessionService(_context, _clock, options),
                new ActiveAccountResolver(_context),
                _clock),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            NewDetector(),
            new UnlockedFailedLoginRecorder(_context, TestSecurityPolicyCache.Over(_context)),
            _clock,
            new TwoFactorCodeRejection(_clockStatus));
    }

    private BruteForceDetector NewDetector()
    {
        return new BruteForceDetector(
            _context,
            new RecordingMessageBus(),
            _clock,
            new OptionsWrapper<BruteForceOptions>(new BruteForceOptions()),
            NullLogger<BruteForceDetector>.Instance);
    }

    /// <summary>The right password and the right code sign in.</summary>
    [Fact]
    public async Task The_right_password_and_the_right_code_sign_in()
    {
        await SeedTotpUserAsync();

        var result = await NewHandler().HandleAsync(Attempt(CurrentCode()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken.Value));
    }

    /// <summary>
    /// A login whose only account is suspended is refused here exactly as a wrong password is — the fix for the hole this
    /// file exists to close. Equality is asserted on the whole <c>Error</c>, not merely on both
    /// having failed, matching how <c>LoginCommandHandlerTests</c> proves the same claim for
    /// <c>/login</c>.
    /// </summary>
    [Fact]
    public async Task A_login_whose_only_account_is_suspended_is_refused_identically_to_a_wrong_password()
    {
        await SeedTotpUserAsync();
        await SuspendTheCustomersAccountAsync();

        var suspended = await NewHandler().HandleAsync(Attempt(CurrentCode()), CancellationToken.None);
        var wrongPassword = await NewHandler().HandleAsync(Attempt(CurrentCode(), password: "wrong"), CancellationToken.None);

        Assert.False(suspended.IsSuccess);
        Assert.Equal(wrongPassword.Error, suspended.Error);
    }

    /// <summary>A login whose only account is suspended issues no session at all, even holding a valid code.</summary>
    [Fact]
    public async Task A_login_whose_only_account_is_suspended_issues_no_session_at_all()
    {
        await SeedTotpUserAsync();
        await SuspendTheCustomersAccountAsync();

        await NewHandler().HandleAsync(Attempt(CurrentCode()), CancellationToken.None);

        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>A wrong password is refused before the code is even considered.</summary>
    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        await SeedTotpUserAsync();

        var result = await NewHandler().HandleAsync(Attempt(CurrentCode(), password: "wrong"), CancellationToken.None);

        Assert.Equal("InvalidCredentialsUnauthorized", result.Error!.Code);
    }

    /// <summary>A wrong code is refused with its own distinct error.</summary>
    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        await SeedTotpUserAsync();

        var result = await NewHandler().HandleAsync(Attempt("000000"), CancellationToken.None);

        Assert.Equal("InvalidTwoFactorCodeUnauthorized", result.Error!.Code);
    }

    /// <summary>
    /// A wrong code on a server whose clock was found not synchronised says the clock may be why,
    /// and is still refused (issue #150).
    /// </summary>
    [Fact]
    public async Task A_wrong_code_with_the_clock_not_synchronised_says_so_and_is_still_refused()
    {
        await SeedTotpUserAsync();
        _clockStatus.Unsynchronized = true;

        var result = await NewHandler().HandleAsync(Attempt("000000"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidTwoFactorCodeClockUnsynchronizedUnauthorized", result.Error!.Code);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>Reads the stored failure count of the seeded login.</summary>
    private async Task<User> ReloadAsync()
    {
        return await _context.Users.AsNoTracking().SingleAsync();
    }

    /// <summary>A wrong code counts against the login, so address rotation cannot bypass the lockout (issue #463).</summary>
    [Fact]
    public async Task A_wrong_code_counts_against_the_login()
    {
        await SeedTotpUserAsync();

        await NewHandler().HandleAsync(Attempt("000000"), CancellationToken.None);

        Assert.Equal(1, (await ReloadAsync()).FailedLoginAttempts);
    }

    /// <summary>A wrong password counts against the login here as it does at sign-in.</summary>
    [Fact]
    public async Task A_wrong_password_counts_against_the_login()
    {
        await SeedTotpUserAsync();

        await NewHandler().HandleAsync(Attempt(CurrentCode(), password: "wrong"), CancellationToken.None);

        Assert.Equal(1, (await ReloadAsync()).FailedLoginAttempts);
    }

    /// <summary>Wrong codes up to the threshold lock the login, and a correct attempt is then refused.</summary>
    [Fact]
    public async Task Wrong_codes_up_to_the_threshold_lock_the_login_and_a_correct_attempt_is_then_refused()
    {
        await SeedTotpUserAsync();
        var threshold = SecurityPolicySnapshot.Default.MaxFailedLoginAttempts;
        for (var attempt = 0; attempt < threshold; attempt++)
        {
            await NewHandler().HandleAsync(Attempt("000000"), CancellationToken.None);
        }

        var locked = await NewHandler().HandleAsync(Attempt(CurrentCode()), CancellationToken.None);

        Assert.True((await ReloadAsync()).IsLockedOut(Now));
        Assert.Equal("InvalidCredentialsUnauthorized", locked.Error!.Code);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>The inverse control: one fewer wrong code than the threshold leaves the login able to sign in.</summary>
    [Fact]
    public async Task One_wrong_code_fewer_than_the_threshold_does_not_lock_the_login()
    {
        await SeedTotpUserAsync();
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts - 1; attempt++)
        {
            await NewHandler().HandleAsync(Attempt("000000"), CancellationToken.None);
        }

        var result = await NewHandler().HandleAsync(Attempt(CurrentCode()), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A correct password on a login with no second factor is a 401 like a wrong one, not a 409 that confirms it.</summary>
    [Fact]
    public async Task A_correct_password_on_a_login_without_two_factor_is_refused_like_a_wrong_password()
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", _hasher.Hash(Password), UserRole.Customer, Now);
        _context.Users.Add(user);
        _context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), user.Id, Guid.NewGuid(), Now));
        await _context.SaveChangesAsync();

        var noFactor = await NewHandler().HandleAsync(Attempt("000000"), CancellationToken.None);
        var wrongPassword = await NewHandler().HandleAsync(Attempt("000000", password: "wrong"), CancellationToken.None);

        Assert.Equal(wrongPassword.Error, noFactor.Error);
        Assert.Equal(2, (await ReloadAsync()).FailedLoginAttempts);
    }
}
