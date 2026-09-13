using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

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
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="sessionService">Performs the rotation.</param>
    /// <param name="accessTokenIssuer">Signs the replacement access token.</param>
    /// <param name="journal">Records a detected token reuse.</param>
    public RefreshSessionCommandHandler(
        IdentityDbContext dbContext,
        ISessionService sessionService,
        IAccessTokenIssuer accessTokenIssuer,
        IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
        _accessTokenIssuer = accessTokenIssuer;
        _journal = journal;
    }

    /// <summary>Rotates the presented token and issues a fresh access token for the same user.</summary>
    /// <param name="command">The token presented, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The new token pair, or a typed failure. A detected reuse is the one refresh outcome worth
    /// auditing: it is the only signal the panel has that a token was copied off a device.
    /// A refresh has no second-factor answer to give — the factor was satisfied when the session was
    /// created — so its body carries no field for one.
    /// </returns>
    public async Task<Result<AuthenticatedOutcome>> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken)
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
                // No claimed name and nothing verified: the caller presented only a token, and the
                // token itself may never be journalled. See IdentityAuditJournal's remarks.
                await _journal.RecordUnidentifiedAsync(
                    AuditActions.RefreshTokenReuseDetected,
                    string.Empty,
                    command.IpAddress,
                    command.UserAgent,
                    cancellationToken);
            }

            return Result<AuthenticatedOutcome>.Fail(rotated.Error);
        }

        var session = await _dbContext.Sessions
            .AsNoTracking()
            .SingleAsync(s => s.Id == rotated.Value.SessionId, cancellationToken);

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null)
        {
            // The user was deleted while the session lived. Nothing to sign for.
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized), ErrorType.Unauthorized));
        }

        // Re-issued rather than inherited, which is what makes a refresh re-evaluate the panel's
        // forced-two-factor policy: an administrator who has just finished enrolling stops being
        // steered on their next refresh, and one whose operator has just turned the policy on starts
        // being steered on theirs.
        var accessToken = await _accessTokenIssuer.IssueAsync(user, rotated.Value.SessionId, cancellationToken);

        return Result<AuthenticatedOutcome>.Ok(new AuthenticatedOutcome(accessToken, user, rotated.Value));
    }
}
