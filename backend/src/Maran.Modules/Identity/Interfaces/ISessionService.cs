using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Interfaces;

/// <summary>
/// Owns the lifecycle of refresh-token sessions: issuing them at login, rotating them on refresh,
/// and revoking them on logout, on an administrator's order, or on detecting a replayed token.
/// </summary>
public interface ISessionService
{
    /// <summary>Issues a brand-new session, starting a fresh rotation family.</summary>
    /// <param name="userId">The user signing in.</param>
    /// <param name="ipAddress">The caller's address, shown on the sessions screen.</param>
    /// <param name="userAgent">The caller's user agent, shown on the sessions screen.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The new session and its plaintext refresh token.</returns>
    Task<IssuedSession> IssueAsync(Guid userId, string ipAddress, string userAgent, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for a new one, revoking the token presented.</summary>
    /// <param name="refreshToken">The token the caller presented.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The replacement session, or a typed failure: <c>RefreshTokenInvalidUnauthorized</c> when the
    /// token is unknown or expired, <c>RefreshTokenReusedUnauthorized</c> when it had already been
    /// rotated — in which case the whole family has just been revoked.
    /// </returns>
    Task<Result<IssuedSession>> RotateAsync(string refreshToken, string ipAddress, string userAgent, CancellationToken cancellationToken);

    /// <summary>Revokes the session a refresh token belongs to.</summary>
    /// <remarks>
    /// Signing out is identified by the refresh token, not by the access token's session claim,
    /// because it must work after the access token has expired — which is precisely when someone
    /// returns to a tab left open overnight and clicks sign out.
    /// </remarks>
    /// <param name="refreshToken">The token the caller presented.</param>
    /// <param name="reason">Why the session is ending.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The id of the user whose session ended, or null when the token matched nothing.</returns>
    Task<Guid?> RevokeByRefreshTokenAsync(string refreshToken, SessionRevocationReason reason, CancellationToken cancellationToken);

    /// <summary>Revokes one session.</summary>
    /// <param name="sessionId">The session to end.</param>
    /// <param name="reason">Why it is ending.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the revocation is stored.</returns>
    Task RevokeAsync(Guid sessionId, SessionRevocationReason reason, CancellationToken cancellationToken);

    /// <summary>Revokes every live session of one user.</summary>
    /// <param name="userId">The user to sign out everywhere.</param>
    /// <param name="reason">Why the sessions are ending.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once every revocation is stored.</returns>
    Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken);
}
