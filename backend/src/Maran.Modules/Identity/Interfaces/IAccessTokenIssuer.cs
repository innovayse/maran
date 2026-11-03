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
    /// <param name="accountId">
    /// The hosting account the token speaks for, written as the account claim every tenant query
    /// follows. It is the session's active account, chosen from the login's active memberships: a login
    /// that holds several accounts has one active at a time. Null writes no claim, which is right for an
    /// administrator and for a customer with no membership.
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
    Task<AccessToken> IssueAsync(User user, Guid sessionId, Guid? accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Issues the one access token of a view-as session: it speaks for <paramref name="viewedUser"/>
    /// with that login's own role, and for the session's account when the login is a customer's or
    /// for the login's reseller when it is a reseller's. It carries the operator as an RFC 8693
    /// <c>act</c> claim and the session kind as <c>sk</c>, and expires with the session (issues #317, #179).
    /// </summary>
    /// <param name="viewedUser">The login being viewed as.</param>
    /// <param name="session">A view-as session (<see cref="Domain.Entities.Session.ViewAs"/> is set) of that login, started by <paramref name="actor"/>.</param>
    /// <param name="actor">The operator viewing.</param>
    /// <returns>The signed token and its expiry; never steered into two-factor enrolment.</returns>
    /// <exception cref="InvalidOperationException">
    /// The session is not a view-as session of these two users, or its facts do not agree with the
    /// viewed login's kind: a customer needs an active account, a reseller needs none and a reseller
    /// id, and an administrator is never viewed as.
    /// </exception>
    AccessToken IssueViewAs(User viewedUser, Session session, User actor);
}
