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

    /// <summary>Signs the access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>Issues the session.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Records the attempt.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Counts refusals per source address and announces an attack.</summary>
    private readonly BruteForceDetector _bruteForceDetector;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies the password.</param>
    /// <param name="totpService">Verifies the TOTP code.</param>
    /// <param name="recoveryCodeService">Verifies a recovery code.</param>
    /// <param name="accessTokenIssuer">Signs the access token.</param>
    /// <param name="sessionService">Issues the session.</param>
    /// <param name="journal">Records the attempt.</param>
    /// <param name="bruteForceDetector">Counts refusals per source address.</param>
    /// <param name="clock">The panel's clock.</param>
    public VerifyTwoFactorCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        IRecoveryCodeService recoveryCodeService,
        IAccessTokenIssuer accessTokenIssuer,
        ISessionService sessionService,
        IdentityAuditJournal journal,
        BruteForceDetector bruteForceDetector,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _recoveryCodeService = recoveryCodeService;
        _accessTokenIssuer = accessTokenIssuer;
        _sessionService = sessionService;
        _journal = journal;
        _bruteForceDetector = bruteForceDetector;
        _clock = clock;
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

        var session = await _sessionService.IssueAsync(user.Id, command.IpAddress, command.UserAgent, cancellationToken);
        var accessToken = await _accessTokenIssuer.IssueAsync(user, session.SessionId, cancellationToken);

        user.RecordLogin(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (usedRecoveryCode)
        {
            // Worth its own entry: a spent recovery code means the user has lost their
            // authenticator, or somebody else has found their codes. Both deserve to be visible.
            await WriteAuditAsync(user.Id, user.Username, AuditActions.RecoveryCodeUsed, command, true, cancellationToken);
        }

        await WriteAuditAsync(user.Id, user.Username, AuditActions.LoginSucceeded, command, true, cancellationToken);

        return Result<AuthenticatedOutcome>.Ok(new AuthenticatedOutcome(accessToken, user, session));
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
