using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

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
    private readonly IAuditWriter _auditWriter;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Verifies the password.</param>
    /// <param name="totpService">Verifies the TOTP code.</param>
    /// <param name="recoveryCodeService">Verifies a recovery code.</param>
    /// <param name="accessTokenIssuer">Signs the access token.</param>
    /// <param name="sessionService">Issues the session.</param>
    /// <param name="auditWriter">Records the attempt.</param>
    /// <param name="clock">The panel's clock.</param>
    public VerifyTwoFactorCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        IRecoveryCodeService recoveryCodeService,
        IAccessTokenIssuer accessTokenIssuer,
        ISessionService sessionService,
        IAuditWriter auditWriter,
        IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _recoveryCodeService = recoveryCodeService;
        _accessTokenIssuer = accessTokenIssuer;
        _sessionService = sessionService;
        _auditWriter = auditWriter;
        _clock = clock;
    }

    /// <summary>Verifies password and code together, then issues the session.</summary>
    /// <param name="command">Both factors, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The signed-in outcome, or a typed failure.</returns>
    public async Task<Result<LoginOutcome>> HandleAsync(VerifyTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Username == command.Username, cancellationToken);

        // The password is re-checked here, not assumed from the first step: this endpoint is
        // reachable on its own, and treating it as "already half authenticated" would turn the
        // second factor into the ONLY factor for anyone who can call it directly.
        if (user is null || !_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized)));
        }

        if (!user.IsTotpEnabled || user.TotpSecret is null)
        {
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.TwoFactorNotEnabledForbidden)));
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
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.InvalidTwoFactorCodeUnauthorized)));
        }

        var session = await _sessionService.IssueAsync(user.Id, command.IpAddress, command.UserAgent, cancellationToken);
        var accessToken = _accessTokenIssuer.Issue(user, session.SessionId);

        user.RecordLogin(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (usedRecoveryCode)
        {
            // Worth its own entry: a spent recovery code means the user has lost their
            // authenticator, or somebody else has found their codes. Both deserve to be visible.
            await WriteAuditAsync(user.Id, user.Username, AuditActions.RecoveryCodeUsed, command, true, cancellationToken);
        }

        await WriteAuditAsync(user.Id, user.Username, AuditActions.LoginSucceeded, command, true, cancellationToken);

        return Result<LoginOutcome>.Ok(new LoginOutcome(
            new LoginResultDto(
                accessToken.Value,
                accessToken.ExpiresAt,
                TwoFactorRequired: false,
                new AuthenticatedUserDto(user.Id, user.Username, user.Email, user.Role, user.AccountId)),
            session));
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
        await _auditWriter.WriteAsync(
            new AuditEntry(userId, username, action, username, command.IpAddress, command.UserAgent, succeeded),
            cancellationToken);
    }
}
