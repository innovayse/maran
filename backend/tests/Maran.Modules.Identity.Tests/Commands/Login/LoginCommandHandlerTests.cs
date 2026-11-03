using Maran.Modules.Identity.Commands.Login;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly RecordingMessageBus _bus = new();
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

    private async Task<User> SeedResellerAsync(string passwordHash, bool suspended = false, bool withTotp = false)
    {
        var login = User.InviteReseller(Guid.NewGuid(), "northwind", "northwind@example.com", Guid.NewGuid(), Now);
        login.Activate(passwordHash);
        if (suspended)
        {
            login.Suspend();
        }

        if (withTotp)
        {
            login.EnableTotp("JBSWY3DPEHPK3PXP");
        }

        _context.Users.Add(login);
        await _context.SaveChangesAsync();
        return login;
    }

    private LoginCommandHandler NewHandler(IPasswordHasher? hasher = null, BruteForceOptions? bruteForce = null)
    {
        var options = new OptionsWrapper<JwtOptions>(new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14,
        });

        return new LoginCommandHandler(
            _context,
            hasher ?? _hasher,
            new AuthenticationCompleter(
                new JwtAccessTokenIssuer(options, TestSecurityPolicyCache.Over(_context), _clock),
                new SessionService(_context, _clock, options),
                new ActiveAccountResolver(_context),
                _clock),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            NewDetector(bruteForce),
            TestSecurityPolicyCache.Over(_context),
            _clock);
    }

    private BruteForceDetector NewDetector(BruteForceOptions? options)
    {
        return new BruteForceDetector(
            _context,
            _bus,
            _clock,
            new OptionsWrapper<BruteForceOptions>(options ?? new BruteForceOptions()),
            NullLogger<BruteForceDetector>.Instance);
    }

    /// <summary>Logging in with the right password returns an access token.</summary>
    [Fact]
    public async Task Logging_in_with_the_right_password_returns_an_access_token()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Authenticated);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Authenticated.AccessToken.Value));
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

        // One assertion where there were three: "a second factor is owed" is now the ABSENCE of the
        // authenticated half, so a token without a session — or a session without a token — is not a
        // state a test has to rule out, because the shape cannot express it.
        Assert.Null(result.Value.Authenticated);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>A successful login writes an audit event and records the login instant.</summary>
    [Fact]
    public async Task A_successful_login_writes_an_audit_event_and_records_the_login_instant()
    {
        var user = await SeedUserAsync(_hasher.Hash(Password));

        await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.Equal(IdentityAuditActions.LoginSucceeded, _audit.Written.Single().Action);
        Assert.Equal(Now, (await _context.Users.SingleAsync(u => u.Id == user.Id)).LastLoginAt);
    }

    /// <summary>A failed login writes an audit event that does not contain the attempted password.</summary>
    [Fact]
    public async Task A_failed_login_writes_an_audit_event_that_does_not_contain_the_attempted_password()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        await NewHandler().HandleAsync(Attempt(password: "hunter2"), CancellationToken.None);

        var entry = _audit.Written.Single();
        Assert.Equal(IdentityAuditActions.LoginFailed, entry.Action);
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
        Assert.Equal(IdentityAuditActions.LoginFailed, entry.Action);
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

        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
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
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Signing in as an invited login is refused with the same result as a wrong password.
    /// </summary>
    /// <remarks>
    /// Equality is asserted on the whole <c>Error</c> record, not merely on both having
    /// failed: an invited login's empty password hash already fails <c>Verify</c>, so this pins that
    /// the state check adds no SECOND, distinguishable refusal on top of it.
    /// </remarks>
    [Fact]
    public async Task Signing_in_as_an_invited_login_is_refused_with_the_same_result_as_a_wrong_password()
    {
        _context.Users.Add(User.Invite(Guid.NewGuid(), "invited", "invited@example.com", Now));
        await _context.SaveChangesAsync();
        await SeedUserAsync(_hasher.Hash(Password));

        var invited = await NewHandler().HandleAsync(Attempt(username: "invited", password: Password), CancellationToken.None);
        var wrongPassword = await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);

        Assert.False(invited.IsSuccess);
        Assert.Equal(wrongPassword.Error, invited.Error);
    }

    /// <summary>
    /// An invited login holding a VALID password hash is still refused, proving the state gate
    /// itself, not merely the empty hash an invited login normally carries.
    /// </summary>
    /// <remarks>
    /// The existing invited-login test above cannot separate "the handler checks state" from "the
    /// handler has no state check at all, and the invitation's empty hash happens to fail
    /// verification anyway" — a handler with the check deleted would still pass it. This constructs
    /// the one case that tells them apart: <see cref="User.ChangePassword"/> sets a real hash without
    /// touching <see cref="UserLifecycle"/>, so the seeded user has the RIGHT password and is still
    /// <see cref="UserLifecycle.Invited"/>. If the state gate were missing, this login would succeed.
    /// </remarks>
    [Fact]
    public async Task An_invited_login_with_a_valid_password_hash_is_still_refused_identically()
    {
        var invited = User.Invite(Guid.NewGuid(), "invited", "invited@example.com", Now);
        invited.ChangePassword(_hasher.Hash(Password));
        _context.Users.Add(invited);
        await _context.SaveChangesAsync();
        await SeedUserAsync(_hasher.Hash(Password));

        var invitedResult = await NewHandler().HandleAsync(Attempt(username: "invited", password: Password), CancellationToken.None);
        var wrongPassword = await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);

        Assert.False(invitedResult.IsSuccess);
        Assert.Equal(wrongPassword.Error, invitedResult.Error);
    }

    /// <summary>
    /// Stores a customer with a valid password whose memberships are the given accounts, each suspended
    /// or active as stated.
    /// </summary>
    /// <param name="withTotp">Whether a second factor is enrolled.</param>
    /// <param name="accounts">Each account the customer owns, and whether its membership is suspended.</param>
    private async Task<User> SeedCustomerAsync(bool withTotp, params (Guid AccountId, bool Suspended)[] accounts)
    {
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", _hasher.Hash(Password), UserRole.Customer, Now);
        if (withTotp)
        {
            customer.EnableTotp("JBSWY3DPEHPK3PXP");
        }

        _context.Users.Add(customer);
        foreach (var (accountId, suspended) in accounts)
        {
            var membership = AccountMembership.Own(Guid.NewGuid(), customer.Id, accountId, Now);
            if (suspended)
            {
                membership.Suspend();
            }

            _context.AccountMemberships.Add(membership);
        }

        await _context.SaveChangesAsync();
        return customer;
    }

    /// <summary>A customer whose only account is suspended is refused the same way a wrong password is.</summary>
    [Fact]
    public async Task Signing_in_with_every_account_suspended_is_refused_with_the_same_result_as_a_wrong_password()
    {
        await SeedCustomerAsync(withTotp: false, (Guid.NewGuid(), true));

        var suspendedResult = await NewHandler().HandleAsync(Attempt(username: "customer"), CancellationToken.None);
        var wrongPassword = await NewHandler().HandleAsync(Attempt(username: "customer", password: "wrong"), CancellationToken.None);

        Assert.False(suspendedResult.IsSuccess);
        Assert.Equal(wrongPassword.Error, suspendedResult.Error);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>
    /// A customer whose every account is suspended is refused at the password step even with a second
    /// factor enrolled, so no code is asked for and nothing about the suspension is learnt.
    /// </summary>
    [Fact]
    public async Task A_second_factor_is_not_asked_for_when_every_account_is_suspended()
    {
        await SeedCustomerAsync(withTotp: true, (Guid.NewGuid(), true));

        var result = await NewHandler().HandleAsync(Attempt(username: "customer"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidCredentialsUnauthorized", result.Error!.Code);
    }

    /// <summary>
    /// A customer with one suspended account and one active one signs in, and the session speaks for
    /// the active one.
    /// </summary>
    [Fact]
    public async Task A_customer_with_one_suspended_and_one_active_account_signs_in_to_the_active_one()
    {
        var active = Guid.NewGuid();
        await SeedCustomerAsync(withTotp: false, (Guid.NewGuid(), true), (active, false));

        var result = await NewHandler().HandleAsync(Attempt(username: "customer"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(active, (await _context.Sessions.SingleAsync()).ActiveAccountId);
    }

    /// <summary>A locked account is refused with the same error a wrong password gets.</summary>
    [Fact]
    public async Task A_locked_account_is_refused_with_the_same_error_a_wrong_password_gets()
    {
        // A distinct "locked" code would tell an attacker the account exists and that their
        // guessing had tripped the lock. Both answers must be the one word: no.
        await SeedUserAsync(_hasher.Hash(Password));
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
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
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        _clock.Advance(SecurityPolicySnapshot.Default.LockoutDuration() + TimeSpan.FromSeconds(1));
        var result = await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Every refused sign-in is counted against the address it came from.</summary>
    [Fact]
    public async Task Every_refused_sign_in_is_counted_against_the_address_it_came_from()
    {
        // The gap this closes: BruteForceDetected had a subscriber, a contract and tests, and
        // nothing anywhere published it, so automatic banning had never once run.
        await SeedUserAsync(_hasher.Hash(Password));
        var handler = NewHandler(bruteForce: new BruteForceOptions { MaxFailuresPerAddress = 3 });

        await handler.HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        await handler.HandleAsync(Attempt(username: "nosuchuser"), CancellationToken.None);
        await handler.HandleAsync(Attempt(password: "wrong"), CancellationToken.None);

        var detection = Assert.IsType<BruteForceDetected>(Assert.Single(_bus.Published));
        Assert.Equal("203.0.113.7", detection.IpAddress);
        Assert.Equal(3, detection.Failures);
    }

    /// <summary>The count crosses usernames so rotating names does not hide an address.</summary>
    [Fact]
    public async Task The_count_crosses_usernames_so_rotating_names_does_not_hide_an_address()
    {
        // The per-user lockout already stops guessing at ONE account and is blind to a script that
        // tries one password against a thousand names. This counter is the one that is not.
        var handler = NewHandler(bruteForce: new BruteForceOptions { MaxFailuresPerAddress = 3 });

        await handler.HandleAsync(Attempt(username: "alice"), CancellationToken.None);
        await handler.HandleAsync(Attempt(username: "bob"), CancellationToken.None);
        await handler.HandleAsync(Attempt(username: "carol"), CancellationToken.None);

        Assert.Single(_bus.Published);
    }

    /// <summary>A refusal by a locked account is counted like any other refusal.</summary>
    [Fact]
    public async Task A_refusal_by_a_locked_account_is_counted_like_any_other_refusal()
    {
        // The lock refuses before the password is read, and an early return past the counter is
        // exactly how an attacker hammering one locked account would become invisible.
        await SeedUserAsync(_hasher.Hash(Password));
        var handler = NewHandler(bruteForce: new BruteForceOptions { MaxFailuresPerAddress = 100 });
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts; attempt++)
        {
            await handler.HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        await handler.HandleAsync(Attempt(), CancellationToken.None);

        var counted = await _context.FailedLoginsByIp.SingleAsync();
        Assert.Equal(SecurityPolicySnapshot.Default.MaxFailedLoginAttempts + 1, counted.Failures);
    }

    /// <summary>A successful sign-in announces nothing at all.</summary>
    [Fact]
    public async Task A_successful_sign_in_announces_nothing_at_all()
    {
        await SeedUserAsync(_hasher.Hash(Password));

        await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.Empty(_bus.Published);
        Assert.Empty(await _context.FailedLoginsByIp.ToListAsync());
    }

    /// <summary>A successful sign-in resets the failures before they reach the threshold.</summary>
    [Fact]
    public async Task A_successful_sign_in_resets_the_failures_before_they_reach_the_threshold()
    {
        var user = await SeedUserAsync(_hasher.Hash(Password));
        for (var attempt = 0; attempt < SecurityPolicySnapshot.Default.MaxFailedLoginAttempts - 1; attempt++)
        {
            await NewHandler().HandleAsync(Attempt(password: "wrong"), CancellationToken.None);
        }

        await NewHandler().HandleAsync(Attempt(), CancellationToken.None);

        Assert.Equal(0, user.FailedLoginAttempts);
    }

    /// <summary>A reseller signs in to a token naming it as a reseller with no account, steered into enrolment by default.</summary>
    [Fact]
    public async Task A_reseller_signs_in_to_a_reseller_token_with_no_account_and_is_steered_into_enrolment()
    {
        var login = await SeedResellerAsync(_hasher.Hash(Password));

        var result = await NewHandler().HandleAsync(Attempt("northwind"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var authenticated = result.Value.Authenticated!;
        Assert.True(authenticated.AccessToken.RequiresTwoFactorSetup);
        Assert.Equal(login.Id, authenticated.User.Id);
        var session = await _context.Sessions.SingleAsync();
        Assert.Null(session.ActiveAccountId);
        Assert.Null(session.ViewAs);
    }

    /// <summary>A suspended reseller with the right password is refused exactly as a wrong password is, and no session is issued.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_as_a_wrong_password_is_and_gets_no_session()
    {
        await SeedResellerAsync(_hasher.Hash(Password), suspended: true);

        var suspended = await NewHandler().HandleAsync(Attempt("northwind"), CancellationToken.None);
        var wrong = await NewHandler().HandleAsync(Attempt("northwind", "wrong"), CancellationToken.None);

        Assert.False(suspended.IsSuccess);
        Assert.Equal(wrong.Error!.Code, suspended.Error!.Code);
        Assert.Equal(wrong.Error.Type, suspended.Error.Type);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>A reseller that never accepted its invitation cannot sign in, even with a password someone typed.</summary>
    [Fact]
    public async Task A_reseller_that_never_accepted_its_invitation_cannot_sign_in()
    {
        var login = User.InviteReseller(Guid.NewGuid(), "northwind", "northwind@example.com", Guid.NewGuid(), Now);
        _context.Users.Add(login);
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Attempt("northwind", Password), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }

    /// <summary>A reseller who enrolled a second factor is asked for it, like an administrator, and gets no session yet.</summary>
    [Fact]
    public async Task A_reseller_with_two_factor_enrolled_gets_no_session_until_the_code_is_verified()
    {
        await SeedResellerAsync(_hasher.Hash(Password), withTotp: true);

        var result = await NewHandler().HandleAsync(Attempt("northwind"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Authenticated);
        Assert.Empty(await _context.Sessions.ToListAsync());
    }
}
