using Maran.Modules.Identity.Domain;

namespace Maran.Modules.Identity.Common.Interfaces;

/// <summary>Signs the short-lived access token a signed-in user carries on every request.</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Issues a token for a user in the context of one session.</summary>
    /// <param name="user">The authenticated user the token speaks for.</param>
    /// <param name="sessionId">
    /// The session this token belongs to, written as the <c>sid</c> claim so a revoked session can
    /// be recognised without waiting for the token itself to expire.
    /// </param>
    /// <returns>The signed token and the instant it expires.</returns>
    AccessToken Issue(User user, Guid sessionId);
}
