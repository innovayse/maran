using Maran.Modules.Identity.Commands.ResetPassword;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.ResetPassword;

/// <summary>Behavioural contract of the password-reset handler.</summary>
public sealed class ResetPasswordCommandHandlerTests : IAsyncLifetime
{
    private const string NewPassword = "correct horse battery staple";

    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly Argon2idPasswordHasher _hasher = new();
    private readonly FakeClock _clock = new(Now);
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>Seeds the user every test resets.</summary>
    public async Task InitializeAsync()
    {
        _context.Users.Add(new User(
            _userId, "admin", "admin@example.com", _hasher.Hash("the old password"), UserRole.Admin, Now));
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

    private ResetPasswordCommandHandler NewHandler()
    {
        return new ResetPasswordCommandHandler(
            _context,
            _hasher,
            NewSessions(),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            _clock);
    }

    private async Task<string> IssueTokenAsync()
    {
        var token = PasswordResetTokenHasher.Generate();
        _context.PasswordResetTokens.Add(
            new PasswordResetToken(Guid.NewGuid(), _userId, PasswordResetTokenHasher.Hash(token), _clock.UtcNow));
        await _context.SaveChangesAsync();
        return token;
    }

    private static ResetPasswordCommand Command(string token)
    {
        return new ResetPasswordCommand(token, NewPassword, "203.0.113.7", "agent");
    }

    /// <summary>A valid token sets the new password and spends itself.</summary>
    [Fact]
    public async Task A_valid_token_sets_the_new_password_and_spends_itself()
    {
        var token = await IssueTokenAsync();

        var result = await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        Assert.True(_hasher.Verify(NewPassword, user.PasswordHash));
        Assert.False(_hasher.Verify("the old password", user.PasswordHash));
        Assert.NotNull((await _context.PasswordResetTokens.SingleAsync()).UsedAt);
    }

    /// <summary>A completed reset revokes every session the account had.</summary>
    /// <remarks>
    /// The protection this pins: a password is reset because it may be in somebody else's hands, and
    /// a stolen refresh cookie outlives a password change unless something ends it. Removing the
    /// revoke-all call must turn this test red.
    /// </remarks>
    [Fact]
    public async Task A_completed_reset_revokes_every_session_the_account_had()
    {
        await NewSessions().IssueAsync(_userId, "198.51.100.4", "thief", CancellationToken.None);
        await NewSessions().IssueAsync(_userId, "203.0.113.7", "owner", CancellationToken.None);
        var token = await IssueTokenAsync();

        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        var sessions = await _context.Sessions.ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session =>
        {
            Assert.Equal(SessionRevocationReason.PasswordChanged, session.RevocationReason);
        });
    }

    /// <summary>A completed reset clears a lockout the forgotten password had caused.</summary>
    [Fact]
    public async Task A_completed_reset_clears_a_lockout_the_forgotten_password_had_caused()
    {
        var policy = SecurityPolicySnapshot.Default;
        var locked = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        for (var attempt = 0; attempt < policy.MaxFailedLoginAttempts; attempt++)
        {
            locked.RecordFailedLogin(Now, policy.MaxFailedLoginAttempts, policy.LockoutDuration());
        }

        await _context.SaveChangesAsync();
        Assert.True(locked.IsLockedOut(Now));
        var token = await IssueTokenAsync();

        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        // Re-queried after the act, never asserted on the instance captured before it. Asserting on
        // that instance passes only because this test shares its context with the handler and EF's
        // identity map hands both the same object; in production the handler holds its own scope,
        // and the same assertion would be reading a stale copy.
        var user = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    /// <summary>A completed reset retires the account's other outstanding tokens.</summary>
    [Fact]
    public async Task A_completed_reset_retires_the_accounts_other_outstanding_tokens()
    {
        var first = await IssueTokenAsync();
        await IssueTokenAsync();

        await NewHandler().HandleAsync(Command(first), CancellationToken.None);

        Assert.All(await _context.PasswordResetTokens.ToListAsync(), stored =>
        {
            Assert.NotNull(stored.UsedAt);
        });
    }

    /// <summary>A token that never existed one that expired and one already spent are refused alike.</summary>
    /// <remarks>
    /// The property this pins is the ABSENCE of a distinction: the three carry the same error code,
    /// so a caller cannot learn from the refusal whether a link ever existed or whether somebody has
    /// already used it.
    /// </remarks>
    [Fact]
    public async Task A_token_that_never_existed_one_that_expired_and_one_already_spent_are_refused_alike()
    {
        var spent = await IssueTokenAsync();
        await NewHandler().HandleAsync(Command(spent), CancellationToken.None);

        var expired = await IssueTokenAsync();
        _clock.Advance(PasswordResetToken.Lifetime + TimeSpan.FromMinutes(1));

        var refusals = new[]
        {
            await NewHandler().HandleAsync(Command(spent), CancellationToken.None),
            await NewHandler().HandleAsync(Command(expired), CancellationToken.None),
            await NewHandler().HandleAsync(Command("not-a-token-anybody-issued"), CancellationToken.None),
        };

        Assert.All(refusals, refusal =>
        {
            Assert.False(refusal.IsSuccess);
            Assert.Equal("PasswordResetTokenInvalid", refusal.Error!.Code);
        });
    }

    /// <summary>A refused token is journalled and its value never reaches the entry.</summary>
    [Fact]
    public async Task A_refused_token_is_journalled_and_its_value_never_reaches_the_entry()
    {
        const string Presented = "not-a-token-anybody-issued";

        await NewHandler().HandleAsync(Command(Presented), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.PasswordResetRefused, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.DoesNotContain(Presented, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Presented, entry.ActorUsername, StringComparison.Ordinal);
        Assert.DoesNotContain(Presented, entry.IpAddress, StringComparison.Ordinal);
        Assert.DoesNotContain(Presented, entry.UserAgent, StringComparison.Ordinal);
    }

    /// <summary>A token matching no row at all names the panel rather than leaving the actor blank.</summary>
    [Fact]
    public async Task A_token_matching_no_row_at_all_names_the_panel_rather_than_leaving_the_actor_blank()
    {
        await NewHandler().HandleAsync(Command("not-a-token-anybody-issued"), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(SystemAuditEntry.NameFor(IdentityAuditJournal.ModuleName), entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
    }

    /// <summary>A spent token names the account it belonged to as the entry's subject.</summary>
    /// <remarks>
    /// "Somebody presented a spent reset link for this account" is the entry that matters, and the
    /// account is the only thing about the attempt that may be written down.
    /// </remarks>
    [Fact]
    public async Task A_spent_token_names_the_account_it_belonged_to_as_the_entrys_subject()
    {
        var token = await IssueTokenAsync();
        await NewHandler().HandleAsync(Command(token), CancellationToken.None);
        _audit.Written.Clear();

        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.PasswordResetRefused, entry.Action);
        Assert.Equal(_userId.ToString(), entry.Subject);
        Assert.DoesNotContain(token, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A completed reset is journalled without the password.</summary>
    [Fact]
    public async Task A_completed_reset_is_journalled_without_the_password()
    {
        var token = await IssueTokenAsync();

        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.PasswordChanged, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(NewPassword, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.ActorUsername, StringComparison.Ordinal);
    }
}
