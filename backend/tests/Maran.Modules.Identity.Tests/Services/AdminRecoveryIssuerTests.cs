using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of <see cref="AdminRecoveryIssuer"/>: the break-glass recovery run from the
/// server's console, with no signed-in principal.
/// </summary>
public sealed class AdminRecoveryIssuerTests : IDisposable
{
    private const string PanelUrl = "https://panel.example.com";

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private AdminRecoveryIssuer NewIssuer(string panelUrl = PanelUrl)
    {
        return new AdminRecoveryIssuer(
            _context,
            new SessionService(_context, _clock, new OptionsWrapper<JwtOptions>(new JwtOptions { RefreshTokenDays = 14 })),
            new RecoveryCodeService(_context, new Argon2idPasswordHasher(), _clock),
            new PasswordResetLinkBuilder(new OptionsWrapper<PanelOptions>(new PanelOptions { PanelUrl = panelUrl })),
            new IdentityAuditJournal(_audit, new NoPrincipalCurrentUser()),
            _clock);
    }

    private async Task<User> SeedAsync(UserRole role = UserRole.Admin, string username = "root")
    {
        var user = new User(Guid.NewGuid(), username, username + "@example.com", "hash", role, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private static string TokenOf(IssuedAdminRecovery issued)
    {
        var link = issued.Link.Reveal();
        return Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
    }

    /// <summary>An administrator found by e-mail address gets a link whose token is stored as a digest only.</summary>
    [Fact]
    public async Task An_administrator_found_by_email_gets_a_link_and_only_the_digest_is_stored()
    {
        var admin = await SeedAsync();

        var result = await NewIssuer().IssueAsync("root@example.com", false, "alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var issued = Recovered(result);
        Assert.Equal("root", issued.Username);
        Assert.False(issued.LinkIsRelative);
        Assert.StartsWith(PanelUrl + "/reset-password?token=", issued.Link.Reveal(), StringComparison.Ordinal);
        Assert.Equal(Now + PasswordResetToken.Lifetime, issued.ExpiresAt);

        var token = TokenOf(issued);
        var stored = Assert.Single(await _context.PasswordResetTokens.Where(row => row.UserId == admin.Id).ToListAsync());
        Assert.Equal(PasswordResetTokenHasher.Hash(token), stored.TokenHash);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.True(stored.IsUsable(Now));
    }

    /// <summary>An administrator is also found by username.</summary>
    [Fact]
    public async Task An_administrator_is_found_by_username()
    {
        await SeedAsync(username: "ops");

        var result = await NewIssuer().IssueAsync("ops", false, "alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ops", Recovered(result).Username);
    }

    /// <summary>With no public address configured the link is root-relative and flagged as such.</summary>
    [Fact]
    public async Task With_no_public_address_the_link_is_relative_and_flagged()
    {
        await SeedAsync();

        var result = await NewIssuer(string.Empty).IssueAsync("root", false, "alice", CancellationToken.None);

        var recovered = Recovered(result);
        Assert.True(recovered.LinkIsRelative);
        Assert.StartsWith("/reset-password?token=", recovered.Link.Reveal(), StringComparison.Ordinal);
    }

    /// <summary>An unknown user is refused as not found when administrators exist.</summary>
    [Fact]
    public async Task An_unknown_user_is_refused_as_not_found()
    {
        await SeedAsync();

        var result = await NewIssuer().IssueAsync("ghost", false, "alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AdminRecoveryUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(await _context.PasswordResetTokens.ToListAsync());
    }

    /// <summary>A customer is refused as not an administrator, and no token is minted.</summary>
    [Fact]
    public async Task A_customer_is_refused_as_not_an_administrator()
    {
        await SeedAsync();
        await SeedAsync(UserRole.Customer, "shopper");

        var result = await NewIssuer().IssueAsync("shopper", false, "alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AdminRecoveryNotAdministrator", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(await _context.PasswordResetTokens.ToListAsync());
    }

    /// <summary>An administrator who has not set a password is refused, and the invitation is not consumed.</summary>
    /// <remarks>
    /// No mutator can leave an administrator unusable, so the row is built the way a legacy one could
    /// be: an invited login whose role is then stated as administrator through the context.
    /// </remarks>
    [Fact]
    public async Task An_administrator_who_has_not_set_a_password_is_refused_and_stays_invited()
    {
        var admin = User.Invite(Guid.NewGuid(), "root", "root@example.com", Now);
        _context.Users.Add(admin);
        _context.Entry(admin).Property(user => user.Role).CurrentValue = UserRole.Admin;
        await _context.SaveChangesAsync();

        var result = await NewIssuer().IssueAsync("root", false, "alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AdminRecoveryLoginNotActive", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(UserLifecycle.Invited, (await _context.Users.SingleAsync()).Lifecycle);
        Assert.Empty(await _context.PasswordResetTokens.ToListAsync());
    }

    /// <summary>With no administrator at all the answer is the no-administrator case, never a token.</summary>
    [Fact]
    public async Task With_no_administrator_yet_the_answer_is_the_no_administrator_case_and_no_token_exists()
    {
        await SeedAsync(UserRole.Customer, "shopper");

        var result = await NewIssuer().IssueAsync("anyone", false, "alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.IsType<NoAdministratorExists>(result.Value);
        Assert.Empty(await _context.PasswordResetTokens.ToListAsync());
    }

    /// <summary>Outstanding reset tokens are spent and replaced by exactly one new usable token.</summary>
    [Fact]
    public async Task Outstanding_tokens_are_spent_and_replaced()
    {
        var admin = await SeedAsync();
        var old = new PasswordResetToken(Guid.NewGuid(), admin.Id, PasswordResetTokenHasher.Hash("old"), Now);
        _context.PasswordResetTokens.Add(old);
        await _context.SaveChangesAsync();

        await NewIssuer().IssueAsync("root", false, "alice", CancellationToken.None);

        var rows = await _context.PasswordResetTokens.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.False(rows.Single(row =>
        {
            return row.Id == old.Id;
        }).IsUsable(Now));
        Assert.Single(rows, row =>
        {
            return row.IsUsable(Now);
        });
    }

    /// <summary>A locked account is unlocked.</summary>
    [Fact]
    public async Task The_lockout_is_cleared()
    {
        var admin = await SeedAsync();
        admin.RecordFailedLogin(Now, 1, TimeSpan.FromHours(1));
        await _context.SaveChangesAsync();
        Assert.True(admin.IsLockedOut(Now));

        await NewIssuer().IssueAsync("root", false, "alice", CancellationToken.None);

        var reloaded = await _context.Users.SingleAsync();
        Assert.False(reloaded.IsLockedOut(Now));
        Assert.Equal(0, reloaded.FailedLoginAttempts);
    }

    /// <summary>Every live session of the administrator is revoked.</summary>
    [Fact]
    public async Task Every_live_session_is_revoked()
    {
        var admin = await SeedAsync();
        _context.Sessions.Add(new Session(
            Guid.NewGuid(), admin.Id, Guid.NewGuid(), "h1", Now, Now.AddDays(1), "203.0.113.7", "agent"));
        _context.Sessions.Add(new Session(
            Guid.NewGuid(), admin.Id, Guid.NewGuid(), "h2", Now, Now.AddDays(1), "203.0.113.8", "agent"));
        await _context.SaveChangesAsync();

        await NewIssuer().IssueAsync("root", false, "alice", CancellationToken.None);

        Assert.All(await _context.Sessions.ToListAsync(), session =>
        {
            Assert.NotNull(session.Revocation);
        });
    }

    /// <summary>Two-factor is left alone unless asked to clear it.</summary>
    [Fact]
    public async Task Two_factor_is_kept_unless_asked()
    {
        var admin = await SeedAsync();
        admin.EnableTotp("SECRET");
        _context.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), admin.Id, "code-hash"));
        await _context.SaveChangesAsync();

        var result = await NewIssuer().IssueAsync("root", false, "alice", CancellationToken.None);

        Assert.False(Recovered(result).TwoFactorCleared);
        Assert.True((await _context.Users.SingleAsync()).IsTotpEnabled);
        Assert.Single(await _context.RecoveryCodes.ToListAsync());
    }

    /// <summary>Asked to, the secret and every recovery code are removed together.</summary>
    [Fact]
    public async Task Clearing_two_factor_removes_the_secret_and_the_recovery_codes()
    {
        var admin = await SeedAsync();
        admin.EnableTotp("SECRET");
        _context.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), admin.Id, "code-hash"));
        await _context.SaveChangesAsync();

        var result = await NewIssuer().IssueAsync("root", true, "alice", CancellationToken.None);

        Assert.True(Recovered(result).TwoFactorCleared);
        var reloaded = await _context.Users.SingleAsync();
        Assert.False(reloaded.IsTotpEnabled);
        Assert.Null(reloaded.TotpSecret);
        Assert.Empty(await _context.RecoveryCodes.ToListAsync());
    }

    /// <summary>One system-actor audit row is written with the user id as subject and the claim marked unverified.</summary>
    [Fact]
    public async Task A_recovery_writes_one_system_actor_row_that_never_carries_the_token()
    {
        var admin = await SeedAsync();

        var result = await NewIssuer().IssueAsync("root", false, "alice\nbob", CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.ConsoleRecovery, entry.Action);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(SystemAuditEntry.NameFor(IdentityAuditJournal.ModuleName), entry.ActorUsername);
        Assert.Equal(admin.Id.ToString(), entry.Subject);
        Assert.Equal(IdentityAuditJournal.ConsoleOrigin, entry.IpAddress);
        Assert.Equal(IdentityAuditJournal.OperatorClaimPrefix + "alicebob", entry.UserAgent);
        Assert.True(entry.Succeeded);

        var token = TokenOf(Recovered(result));
        var digest = PasswordResetTokenHasher.Hash(token);
        foreach (var field in new[] { entry.ActorUsername, entry.Action, entry.Subject, entry.IpAddress, entry.UserAgent })
        {
            Assert.DoesNotContain(token, field, StringComparison.Ordinal);
            Assert.DoesNotContain(digest, field, StringComparison.Ordinal);
        }
    }

    /// <summary>A refusal is journalled as a failed entry, and the typed refusals leave no other change.</summary>
    [Fact]
    public async Task A_refusal_is_journalled_as_failed()
    {
        await SeedAsync();

        await NewIssuer().IssueAsync("ghost", false, "alice", CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.False(entry.Succeeded);
        Assert.Equal("ghost", entry.Subject);
    }

    /// <summary>Reads the recovery out of a successful issue, failing the test when it is anything else.</summary>
    /// <param name="result">What the issuer answered.</param>
    /// <returns>The issued recovery.</returns>
    private static IssuedAdminRecovery Recovered(Result<AdminRecoveryOutcome> result)
    {
        Assert.True(result.IsSuccess);
        return Assert.IsType<AdminRecoveryIssued>(result.Value).Recovery;
    }

    /// <summary>An <see cref="ICurrentUser"/> double for the console, where nobody is signed in.</summary>
    private sealed class NoPrincipalCurrentUser : ICurrentUser
    {
        /// <summary>Nobody.</summary>
        public Caller Caller { get; } = AnonymousCaller.Instance;
    }
}
