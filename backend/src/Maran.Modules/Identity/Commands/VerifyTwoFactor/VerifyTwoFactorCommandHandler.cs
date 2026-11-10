using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.VerifyTwoFactor;

/// <summary>Handles <see cref="VerifyTwoFactorCommand"/> by checking both factors and signing in.</summary>
public sealed class VerifyTwoFactorCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the password.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Verifies the TOTP code.</summary>
    private readonly ITotpService _totpService;

    /// <summary>Verifies a recovery code when the authenticator is gone.</summary>
    private readonly IRecoveryCodeService _recoveryCodeService;

    /// <summary>
    /// The one place that checks whether this login may sign in and, when it may, issues its session
    /// and access token. See its own remarks for why this handler does not check the state itself —
    /// this is precisely the check that used to be missing here, letting a suspended login with
    /// two-factor enrolled sign in through this endpoint after its sessions were revoked.
    /// </summary>
    private readonly AuthenticationCompleter _authenticationCompleter;

    /// <summary>Records the attempt.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Counts refusals per source address and announces an attack.</summary>
    private readonly BruteForceDetector _bruteForceDetector;

    /// <summary>Counts a failed password or code against the login, serialised with every concurrent one.</summary>
    private readonly IFailedLoginRecorder _failedLoginRecorder;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The answer to a refused code, which says when the server's clock may be why.</summary>
    private readonly TwoFactorCodeRejection _rejection;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies the password.</param>
    /// <param name="totpService">Verifies the TOTP code.</param>
    /// <param name="recoveryCodeService">Verifies a recovery code.</param>
    /// <param name="authenticationCompleter">Checks sign-in eligibility and issues the session.</param>
    /// <param name="journal">Records the attempt.</param>
    /// <param name="bruteForceDetector">Counts refusals per source address.</param>
    /// <param name="failedLoginRecorder">Counts a failed password or code against the login.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="rejection">The answer to a refused code.</param>
    public VerifyTwoFactorCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        IRecoveryCodeService recoveryCodeService,
        AuthenticationCompleter authenticationCompleter,
        IdentityAuditJournal journal,
        BruteForceDetector bruteForceDetector,
        IFailedLoginRecorder failedLoginRecorder,
        IClock clock,
        TwoFactorCodeRejection rejection)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _recoveryCodeService = recoveryCodeService;
        _authenticationCompleter = authenticationCompleter;
        _journal = journal;
        _bruteForceDetector = bruteForceDetector;
        _failedLoginRecorder = failedLoginRecorder;
        _clock = clock;
        _rejection = rejection;
    }

    /// <summary>Verifies password and code together, then issues the session.</summary>
    /// <param name="command">Both factors, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The signed-in outcome, or a typed failure. There is no third answer: this endpoint cannot owe
    /// a second factor — it IS the second factor — so its body has no field to say so.
    /// </returns>
    public async Task<Result<AuthenticatedOutcome>> HandleAsync(VerifyTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Username == command.Username, cancellationToken);

        // A locked account is refused before its password or code is looked at, with the same error a
        // wrong password gets (see LoginCommandHandler for why the answer must not differ). Without
        // this check the endpoint was a way round the per-account lockout: an attacker rotating
        // source addresses guessed the password or the one-time code here, where nothing counted
        // them per login (issue #463).
        if (user is not null && user.IsLockedOut(_clock.UtcNow))
        {
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        // The password is re-checked here, not assumed from the first step: this endpoint is
        // reachable on its own, and treating it as "already half authenticated" would turn the
        // second factor into the ONLY factor for anyone who can call it directly.
        if (user is null || !_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            // Counted, because this endpoint takes a password and can be called on its own: an
            // attacker who guessed passwords here rather than at /login would be invisible to the
            // detector otherwise, which is a bypass of the whole ban path rather than a gap in it.
            // Counted against the login as well as the address, because the address counter cannot
            // see an attacker who rotates addresses.
            if (user is not null)
            {
                await _failedLoginRecorder.RecordAsync(user, _clock.UtcNow, cancellationToken);
            }

            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        // Same refusal as a wrong password, deliberately, and checked before anything else about
        // this user is revealed or written: a distinct answer would tell an attacker which suspended
        // account still has a matching password. This is the check that was missing here — see the
        // field's own remarks — so a suspended login with two-factor enrolled could still mint a
        // fresh session through this endpoint after AccountSuspendingHandler had revoked every other
        // one. AuthenticationCompleter enforces it again below; the two calls answer the identical
        // question so neither can silently stop asking it. A customer whose every account is
        // suspended is refused here too, for the same reason (issue #326).
        if (!await _authenticationCompleter.MayCompleteAsync(user, cancellationToken))
        {
            await _failedLoginRecorder.RecordAsync(user, _clock.UtcNow, cancellationToken);
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        // A correct password on an account with no second factor gets the answer a wrong password
        // gets: a distinct 409 told the caller the password was right (issue #463). It is counted
        // like any other failed attempt, since this endpoint is not how such an account signs in.
        if (user.TotpSecret is not { } totpSecret)
        {
            await _failedLoginRecorder.RecordAsync(user, _clock.UtcNow, cancellationToken);
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        var usedRecoveryCode = false;
        if (_totpService.Verify(totpSecret, command.Code, user.LastTotpWindow, out var window))
        {
            user.RecordTotpWindow(window);
        }
        else if (await _recoveryCodeService.ConsumeAsync(user.Id, command.Code, cancellationToken))
        {
            usedRecoveryCode = true;
        }
        else
        {
            await _failedLoginRecorder.RecordAsync(user, _clock.UtcNow, cancellationToken);
            await WriteAuditAsync(user.Id, user.Username, IdentityAuditActions.LoginFailed, command, false, cancellationToken);
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(await _rejection.SignInErrorAsync(cancellationToken));
        }

        var completed = await _authenticationCompleter.CompleteAsync(
            user, command.IpAddress, command.UserAgent, cancellationToken);
        if (!completed.IsSuccess)
        {
            // The state check above already refused a non-Active user, so this is unreachable in
            // practice; kept because this handler must not silently drop a refusal the completer
            // ever does report.
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return completed;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (usedRecoveryCode)
        {
            // Worth its own entry: a spent recovery code means the user has lost their
            // authenticator, or somebody else has found their codes. Both deserve to be visible.
            await WriteAuditAsync(user.Id, user.Username, IdentityAuditActions.RecoveryCodeUsed, command, true, cancellationToken);
        }

        await WriteAuditAsync(user.Id, user.Username, IdentityAuditActions.LoginSucceeded, command, true, cancellationToken);

        return completed;
    }

    /// <summary>Writes one journal entry for this attempt.</summary>
    /// <param name="userId">The user involved.</param>
    /// <param name="username">Their login name.</param>
    /// <param name="action">What happened, from the Sdk's <c>*AuditActions</c> constants.</param>
    /// <param name="command">The attempt, for its address and client. Its secrets never travel.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task WriteAuditAsync(
        Guid userId,
        string username,
        string action,
        VerifyTwoFactorCommand command,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _journal.RecordClaimAsync(
            userId,
            username,
            action,
            command.IpAddress,
            command.UserAgent,
            succeeded,
            cancellationToken);
    }
}
