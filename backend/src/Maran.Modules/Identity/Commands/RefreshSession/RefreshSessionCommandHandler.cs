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

    /// <summary>Re-checks the account the session speaks for against the login's memberships.</summary>
    private readonly ActiveAccountResolver _activeAccountResolver;

    /// <summary>Records a detected token reuse; an ordinary refresh is not journal-worthy.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="sessionService">Performs the rotation.</param>
    /// <param name="accessTokenIssuer">Signs the replacement access token.</param>
    /// <param name="activeAccountResolver">Re-checks the account the session speaks for.</param>
    /// <param name="journal">Records a detected token reuse.</param>
    public RefreshSessionCommandHandler(
        IdentityDbContext dbContext,
        ISessionService sessionService,
        IAccessTokenIssuer accessTokenIssuer,
        ActiveAccountResolver activeAccountResolver,
        IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
        _accessTokenIssuer = accessTokenIssuer;
        _activeAccountResolver = activeAccountResolver;
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
            .SingleAsync(s => s.Id == rotated.Value.SessionId, cancellationToken);

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null)
        {
            // The user was deleted while the session lived. Nothing to sign for.
            return Result<AuthenticatedOutcome>.Fail(Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized), ErrorType.Unauthorized));
        }

        // "May this login be used at all" — asked HERE and not left to the session row's validity
        // (issue #58). A refresh mints a working access token from a credential that was verified in
        // the past, so it is a way in, and it consulted nothing but whether the user still existed.
        //
        // Relying on revocation instead was a narrower guarantee than it looked. AccountSuspendingHandler
        // writes the state and revokes the sessions as two separate operations — Suspend() and
        // SaveChangesAsync first, RevokeAllAsync after — so anything that prevents the second leaves a
        // suspended login holding live sessions, and each refresh would rotate into a fresh token
        // indefinitely. An Invited login is refused here for the same reason: it holds no password,
        // so a session in its name is one nothing should have issued.
        //
        // The refusal is this path's own RefreshTokenInvalidUnauthorized rather than the sign-in
        // paths' InvalidCredentials: the caller presented a token and learns only that the token is
        // no good, which is the same thing every other refusal on this endpoint says. The session is
        // left rotated — the presented token is already spent, and a suspended login gaining a spent
        // token back would be the wrong way to fail.
        if (!user.MayAuthenticate)
        {
            return Result<AuthenticatedOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized), ErrorType.Unauthorized));
        }

        // Re-issued rather than inherited, which is what makes a refresh re-evaluate the panel's
        // forced-two-factor policy: an administrator who has just finished enrolling stops being
        // steered on their next refresh, and one whose operator has just turned the policy on starts
        // being steered on theirs.
        //
        // The account is re-resolved for the same reason: the rotation carried the session's choice
        // forward, but the login may have lost its membership of that account since (it was removed,
        // or the account was deleted), and a refresh must not keep acting for an account the login
        // no longer holds. The corrected choice is stored, so the next refresh starts from it.
        var activeAccountId = await _activeAccountResolver.ResolveAsync(user, session.ActiveAccountId, cancellationToken);
        if (activeAccountId != session.ActiveAccountId)
        {
            session.SwitchAccount(activeAccountId);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var accessToken = await _accessTokenIssuer.IssueAsync(
            user, rotated.Value.SessionId, activeAccountId, cancellationToken);

        return Result<AuthenticatedOutcome>.Ok(
            new AuthenticatedOutcome(accessToken, user, rotated.Value with { ActiveAccountId = activeAccountId }));
    }
}
