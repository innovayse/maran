using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.RefreshSession;

/// <summary>Handles <see cref="RefreshSessionCommand"/> by rotating the session and re-signing.</summary>
public sealed class RefreshSessionCommandHandler
{
    /// <summary>The module's database context, used to load the user the rotated session belongs to.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Performs the rotation, including reuse detection.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Signs the replacement access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>Records a detected token reuse; an ordinary refresh is not journal-worthy.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="sessionService">Performs the rotation.</param>
    /// <param name="accessTokenIssuer">Signs the replacement access token.</param>
    /// <param name="auditWriter">Records a detected token reuse.</param>
    public RefreshSessionCommandHandler(
        IdentityDbContext dbContext,
        ISessionService sessionService,
        IAccessTokenIssuer accessTokenIssuer,
        IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
        _accessTokenIssuer = accessTokenIssuer;
        _auditWriter = auditWriter;
    }

    /// <summary>Rotates the presented token and issues a fresh access token for the same user.</summary>
    /// <param name="command">The token presented, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The new token pair, or a typed failure. A detected reuse is the one refresh outcome worth
    /// auditing: it is the only signal the panel has that a token was copied off a device.
    /// </returns>
    public async Task<Result<LoginOutcome>> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        var rotated = await _sessionService.RotateAsync(
            command.RefreshToken,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        if (!rotated.IsSuccess)
        {
            if (rotated.Error!.Code == nameof(ErrorMessages.RefreshTokenReusedUnauthorized))
            {
                await _auditWriter.WriteAsync(
                    new AuditEntry(
                        null,
                        string.Empty,
                        AuditActions.RefreshTokenReuseDetected,
                        string.Empty,
                        command.IpAddress,
                        command.UserAgent,
                        Succeeded: false),
                    cancellationToken);
            }

            return Result<LoginOutcome>.Fail(rotated.Error);
        }

        var session = await _dbContext.Sessions
            .AsNoTracking()
            .SingleAsync(s => s.Id == rotated.Value.SessionId, cancellationToken);

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null)
        {
            // The user was deleted while the session lived. Nothing to sign for.
            return Result<LoginOutcome>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized)));
        }

        var accessToken = _accessTokenIssuer.Issue(user, rotated.Value.SessionId);

        return Result<LoginOutcome>.Ok(new LoginOutcome(
            new LoginResultDto(
                accessToken.Value,
                accessToken.ExpiresAt,
                TwoFactorRequired: false,
                new AuthenticatedUserDto(user.Id, user.Username, user.Email, user.Role, user.AccountId)),
            rotated.Value));
    }
}
