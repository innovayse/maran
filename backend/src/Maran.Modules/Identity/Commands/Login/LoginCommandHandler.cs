using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.Login;

/// <summary>Handles <see cref="LoginCommand"/>: verifies the password, then issues a session.</summary>
public sealed class LoginCommandHandler
{
    /// <summary>
    /// An Argon2id hash of a password nobody knows, verified against when the username does not
    /// exist. Without it, a miss returns in microseconds and a hit takes the full cost of the KDF,
    /// so the response time answers "does this account exist?" — and the identical error message
    /// above it would have been for nothing.
    /// </summary>
    private const string DummyHash =
        "$argon2id$v=19$m=65536,t=3,p=2$c2FsdHNhbHRzYWx0c2Ex$Xn7Ux4o0aMbEwbFbNRkUdQGmiWJDLQdEPGqbYAoOKzs";

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the password and reports when its hash needs upgrading.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>
    /// The one place that checks whether this login may sign in and, when it may, issues its session
    /// and access token. See its own remarks for why this handler does not check the state itself.
    /// </summary>
    private readonly AuthenticationCompleter _authenticationCompleter;

    /// <summary>Records the attempt, successful or not.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Counts refusals per source address and announces an attack.</summary>
    private readonly BruteForceDetector _bruteForceDetector;

    /// <summary>The panel's security policy, read for the account lockout numbers.</summary>
    private readonly SecurityPolicyCache _policyCache;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies the password.</param>
    /// <param name="authenticationCompleter">Checks sign-in eligibility and issues the session.</param>
    /// <param name="journal">Records the attempt.</param>
    /// <param name="bruteForceDetector">Counts refusals per source address.</param>
    /// <param name="policyCache">The panel's security policy, read for the account lockout numbers.</param>
    /// <param name="clock">The panel's clock.</param>
    public LoginCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        AuthenticationCompleter authenticationCompleter,
        IdentityAuditJournal journal,
        BruteForceDetector bruteForceDetector,
        SecurityPolicyCache policyCache,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _authenticationCompleter = authenticationCompleter;
        _journal = journal;
        _bruteForceDetector = bruteForceDetector;
        _policyCache = policyCache;
        _clock = clock;
    }

    /// <summary>Verifies the credentials and, unless a second factor is owed, issues a session.</summary>
    /// <param name="command">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The signed-in outcome, or <c>InvalidCredentialsUnauthorized</c> — one error for both an
    /// unknown username and a wrong password, so the endpoint answers no question about who exists.
    /// </returns>
    public async Task<Result<LoginOutcome>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Username == command.Username, cancellationToken);

        if (user is null)
        {
            // Spend the same time a real verification would, then fail identically.
            _passwordHasher.Verify(command.Password, DummyHash);
            return await RefuseAsync(null, command, cancellationToken);
        }

        // A locked account is refused before its password is even looked at, and refused with the
        // SAME error a wrong password gets. A distinct "locked" answer would be an oracle: it tells
        // an attacker the account exists, and tells them their guessing is working well enough to
        // have tripped the lock. The person actually locked out learns nothing from the response
        // either — which is the cost, paid deliberately, and why the window is short.
        if (user.IsLockedOut(_clock.UtcNow))
        {
            return await RefuseAsync(user, command, cancellationToken);
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            // Counted on the user row, not in memory: the per-address rate limit cannot see an
            // attacker who rotates addresses, and this is the counter that can.
            // The threshold and the window come from the panel's operator-configurable policy, not
            // from constants on the entity: the lockout used to be a recompile.
            var policy = await _policyCache.GetAsync(cancellationToken);
            user.RecordFailedLogin(_clock.UtcNow, policy.MaxFailedLoginAttempts, policy.LockoutDuration());
            await _dbContext.SaveChangesAsync(cancellationToken);

            return await RefuseAsync(user, command, cancellationToken);
        }

        // Same refusal as a wrong password, deliberately. A distinct "your account is suspended"
        // tells an attacker which usernames exist and which of them belong to real customers. An
        // invited user's empty password hash already fails verification above, so this is what
        // makes a SUSPENDED user refuse too, and what makes the invited case refuse for a stated
        // reason rather than by accident.
        //
        // Checked BEFORE the rehash below, and before any other write: a refused sign-in must not
        // touch the database, and a rehash performed here would be exactly that for a suspended
        // account whose correct-but-stale password happens to verify.
        if (user.State != UserState.Active)
        {
            return await RefuseAsync(user, command, cancellationToken);
        }

        // The one moment the plaintext password is known to be correct, and therefore the only
        // moment a hash raised to stronger parameters can be recomputed without asking the user.
        if (_passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.ChangePassword(_passwordHasher.Hash(command.Password));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (user.IsTotpEnabled)
        {
            // Deliberately no session and no token: the second factor has not been shown yet, and
            // issuing anything now would make the factor optional for anyone holding the password.
            return Result<LoginOutcome>.Ok(new LoginOutcome(null));
        }

        // The state gate lives in AuthenticationCompleter, not here — see its own remarks. The
        // check above already refused a non-Active user, so this call always succeeds in practice;
        // it is made through the shared completer anyway so this handler cannot drift from
        // VerifyTwoFactorCommandHandler's path to the same outcome.
        var completed = await _authenticationCompleter.CompleteAsync(
            user, command.IpAddress, command.UserAgent, cancellationToken);
        if (!completed.IsSuccess)
        {
            return await RefuseAsync(user, command, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordClaimAsync(
            user.Id,
            user.Username,
            AuditActions.LoginSucceeded,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<LoginOutcome>.Ok(new LoginOutcome(completed.Value));
    }

    /// <summary>Records a refused attempt, counts it against its source address, and answers no.</summary>
    /// <param name="user">The user, when the name matched one; null when it did not.</param>
    /// <param name="command">The attempt. Its password never reaches the journal.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The one refusal every failed sign-in returns, whatever it actually failed on.</returns>
    /// <remarks>
    /// <para>
    /// <b>All three refusals go through here, and that is the point.</b> A sign-in can be refused for
    /// an unknown name, a locked account or a wrong password, and each of the three has to leave the
    /// same trace, be counted the same way, and return the same error — a distinct answer to any of
    /// them would tell a caller which of their guesses was closest. Keeping the three obligations in
    /// one place is what stops a fourth refusal added later from silently having none of them: the
    /// brute-force counter existed as a contract for a whole release without a producer, and an
    /// early <c>return</c> that walks past a two-line block is exactly how that happens again.
    /// </para>
    /// <para>
    /// The counting comes after the journal entry, so a detection is never announced for an attempt
    /// the journal has no record of.
    /// </para>
    /// </remarks>
    private async Task<Result<LoginOutcome>> RefuseAsync(
        User? user,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        await _journal.RecordClaimAsync(
            user?.Id,
            command.Username,
            AuditActions.LoginFailed,
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);

        return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
    }
}
