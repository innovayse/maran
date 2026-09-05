using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Interfaces;

/// <summary>Signs the short-lived access token a signed-in user carries on every request.</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Issues a token for a user in the context of one session.</summary>
    /// <param name="user">The authenticated user the token speaks for.</param>
    /// <param name="sessionId">
    /// The session this token belongs to, written as the <c>sid</c> claim so a revoked session can
    /// be recognised without waiting for the token itself to expire.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the read of the panel's security policy.</param>
    /// <returns>The signed token, the instant it expires, and whether its holder is steered into enrolment.</returns>
    /// <remarks>
    /// Asynchronous because the forced-two-factor decision is part of issuing a token, and that
    /// decision reads the panel's security policy. Deliberately not a parameter every caller passes:
    /// there are three places a token is issued — login, second-factor verification and refresh —
    /// and a steering flag one of them could forget to thread through is a steering flag a refresh
    /// silently clears. Making it the issuer's own job means every present and future issuing path
    /// gets it without remembering to.
    /// </remarks>
    Task<AccessToken> IssueAsync(User user, Guid sessionId, CancellationToken cancellationToken);
}
