using Maran.Modules.Identity.Commands.VerifyTwoFactor;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
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
        await _context.SaveChangesAsync();
        return user;
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
                _clock),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            NewDetector());
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
    /// A suspended login is refused here exactly as a wrong password is — the fix for the hole this
    /// file exists to close. Equality is asserted on the whole <c>Error</c>, not merely on both
    /// having failed, matching how <c>LoginCommandHandlerTests</c> proves the same claim for
    /// <c>/login</c>.
    /// </summary>
    [Fact]
    public async Task A_suspended_login_is_refused_identically_to_a_wrong_password()
    {
        var user = await SeedTotpUserAsync();
        user.Suspend();
        await _context.SaveChangesAsync();

        var suspended = await NewHandler().HandleAsync(Attempt(CurrentCode()), CancellationToken.None);
        var wrongPassword = await NewHandler().HandleAsync(Attempt(CurrentCode(), password: "wrong"), CancellationToken.None);

        Assert.False(suspended.IsSuccess);
        Assert.Equal(wrongPassword.Error, suspended.Error);
    }

    /// <summary>A suspended login issues no session at all, even holding a valid code.</summary>
    [Fact]
    public async Task A_suspended_login_issues_no_session_at_all()
    {
        var user = await SeedTotpUserAsync();
        user.Suspend();
        await _context.SaveChangesAsync();

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
}
