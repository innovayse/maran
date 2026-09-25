using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one place that answers "may this login be used" and, when it may, issues the session and
/// access token that follow from a yes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists rather than a repeated <c>if</c>.</b> <c>LoginCommandHandler</c> and
/// <c>VerifyTwoFactorCommandHandler</c> are two independent paths to the same outcome — a signed-in
/// session — and each once carried its own copy of "the state must be
/// <see cref="UserState.Active"/>". A copy is a place the next one can be missing: the two-factor
/// endpoint's copy was, until it was found in review, which let a suspended login with two-factor
/// enrolled sign in through it after its sessions had been revoked. Both sign-in handlers now route
/// every session they issue through this single method.
/// </para>
/// <para>
/// <b>This is not a structural guarantee, and no comment here should claim it is.</b>
/// <see cref="IAccessTokenIssuer"/> and <see cref="ISessionService"/> are ordinary services in the
/// shared container, already injected directly by <c>ResetPasswordCommandHandler</c> and
/// <c>RefreshSessionCommandHandler</c> for their own, legitimate reasons. Nothing stops a future
/// handler from doing the same and issuing a session with no state check at all — the gate this type
/// provides is held by convention and by review noticing a new handler bypasses it, not by anything
/// the compiler or the container enforces. A comment claiming otherwise would be worse than none: it
/// would tell the next reviewer there is nothing here to check.
/// </para>
/// <para>
/// <b>The refusal is byte-identical to a wrong password</b>, by construction: it is the same
/// <see cref="ErrorMessages.InvalidCredentialsUnauthorized"/> every other sign-in refusal in this
/// module returns, so a caller learns nothing about which of their guesses was closest
/// (rules/security.md).
/// </para>
/// <para>
/// <b>Deliberately does not touch the database or the journal.</b> <see cref="User.RecordLogin"/> is
/// called here because it is part of "what a successful sign-in does", but persisting it and writing
/// the audit entry stay with the caller: the two handlers disagree on the audit action name and on
/// what else shares their <c>SaveChangesAsync</c>, and folding those in here would make this method
/// know about callers it should not have to.
/// </para>
/// </remarks>
public sealed class AuthenticationCompleter
{
    /// <summary>Signs the access token.</summary>
    private readonly IAccessTokenIssuer _accessTokenIssuer;

    /// <summary>Issues the refresh-token session.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the completer.</summary>
    /// <param name="accessTokenIssuer">Signs the access token.</param>
    /// <param name="sessionService">Issues the refresh-token session.</param>
    /// <param name="clock">The panel's clock.</param>
    public AuthenticationCompleter(IAccessTokenIssuer accessTokenIssuer, ISessionService sessionService, IClock clock)
    {
        _accessTokenIssuer = accessTokenIssuer;
        _sessionService = sessionService;
        _clock = clock;
    }

    /// <summary>
    /// Refuses a login whose state is not <see cref="UserState.Active"/>; otherwise issues a session
    /// and an access token and records the login on <paramref name="user"/>.
    /// </summary>
    /// <param name="user">The user whose credentials — password, and second factor where owed — have
    /// already been verified. Only the STATE remains to be checked; this method checks nothing else.</param>
    /// <param name="ipAddress">The caller's address, stamped on the issued session.</param>
    /// <param name="userAgent">The caller's user agent, stamped on the issued session.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The signed-in outcome, or the one refusal every failed sign-in in this module returns. The
    /// caller still owes <c>SaveChangesAsync</c> and its own audit entry.
    /// </returns>
    public async Task<Result<AuthenticatedOutcome>> CompleteAsync(
        User user,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        // Same refusal as a wrong password, deliberately — see the type's remarks. A suspended
        // login's sessions were already revoked when it was suspended; this is what stops a fresh
        // one being minted through whichever endpoint is asked, two-factor included.
        if (user.State != UserState.Active)
        {
            return Result<AuthenticatedOutcome>.Fail(
                Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized), ErrorType.Unauthorized));
        }

        var session = await _sessionService.IssueAsync(user.Id, ipAddress, userAgent, cancellationToken);
        var accessToken = await _accessTokenIssuer.IssueAsync(user, session.SessionId, cancellationToken);

        user.RecordLogin(_clock.UtcNow);

        return Result<AuthenticatedOutcome>.Ok(new AuthenticatedOutcome(accessToken, user, session));
    }
}
