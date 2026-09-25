using Maran.Modules.Identity.Domain.Enums;
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

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies the password.</param>
    /// <param name="totpService">Verifies the TOTP code.</param>
    /// <param name="recoveryCodeService">Verifies a recovery code.</param>
    /// <param name="authenticationCompleter">Checks sign-in eligibility and issues the session.</param>
    /// <param name="journal">Records the attempt.</param>
    /// <param name="bruteForceDetector">Counts refusals per source address.</param>
    public VerifyTwoFactorCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        IRecoveryCodeService recoveryCodeService,
        AuthenticationCompleter authenticationCompleter,
        IdentityAuditJournal journal,
        BruteForceDetector bruteForceDetector)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _recoveryCodeService = recoveryCodeService;
        _authenticationCompleter = authenticationCompleter;
        _journal = journal;
        _bruteForceDetector = bruteForceDetector;
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

        // The password is re-checked here, not assumed from the first step: this endpoint is
        // reachable on its own, and treating it as "already half authenticated" would turn the
        // second factor into the ONLY factor for anyone who can call it directly.
        if (user is null || !_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            // Counted, because this endpoint takes a password and can be called on its own: an
            // attacker who guessed passwords here rather than at /login would be invisible to the
            // detector otherwise, which is a bypass of the whole ban path rather than a gap in it.
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        // Same refusal as a wrong password, deliberately, and checked before anything else about
        // this user is revealed or written: a distinct answer would tell an attacker which suspended
        // account still has a matching password. This is the check that was missing here — see the
        // field's own remarks — so a suspended login with two-factor enrolled could still mint a
        // fresh session through this endpoint after AccountSuspendingHandler had revoked every other
        // one. AuthenticationCompleter enforces it again below; the two calls answer the identical
        // question so neither can silently stop asking it.
        if (user.State != UserState.Active)
        {
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        if (!user.IsTotpEnabled || user.TotpSecret is null)
        {
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.TwoFactorNotEnabledForbidden), ErrorType.Forbidden));
        }

        var usedRecoveryCode = false;
        if (_totpService.Verify(user.TotpSecret, command.Code, user.LastTotpWindow, out var window))
        {
            user.RecordTotpWindow(window);
        }
        else if (await _recoveryCodeService.ConsumeAsync(user.Id, command.Code, cancellationToken))
        {
            usedRecoveryCode = true;
        }
        else
        {
            await WriteAuditAsync(user.Id, user.Username, AuditActions.LoginFailed, command, false, cancellationToken);
            await _bruteForceDetector.RecordFailureAsync(command.IpAddress, cancellationToken);
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidTwoFactorCodeUnauthorized), ErrorType.Unauthorized));
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
            await WriteAuditAsync(user.Id, user.Username, AuditActions.RecoveryCodeUsed, command, true, cancellationToken);
        }

        await WriteAuditAsync(user.Id, user.Username, AuditActions.LoginSucceeded, command, true, cancellationToken);

        return completed;
    }

    /// <summary>Writes one journal entry for this attempt.</summary>
    /// <param name="userId">The user involved.</param>
    /// <param name="username">Their login name.</param>
    /// <param name="action">What happened, from <see cref="AuditActions"/>.</param>
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
