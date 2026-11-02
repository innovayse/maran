using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Domain.ValueObjects;
namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One link in a refresh-token chain: the server side of a signed-in device. Sessions live in
/// PostgreSQL so they can be listed and revoked — by their owner and by an administrator
/// (spec §10) — which a stateless token alone could never allow.
/// </summary>
public sealed class Session
{
    /// <summary>The session's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user this session signs in.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The rotation chain this session belongs to.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>SHA-256 of the refresh token.</summary>
    public string TokenHash { get; private set; }

    /// <summary>The instant the session was created.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>The instant the refresh token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// How and when the session ended before its expiry, or <c>null</c> while it has not been
    /// revoked. Instant and reason exist together or not at all, which is why they are one value.
    /// </summary>
    public SessionRevocation? Revocation { get; private set; }

    /// <summary>The address the session was created from.</summary>
    public string IpAddress { get; private set; }

    /// <summary>The user agent the session was created from.</summary>
    public string UserAgent { get; private set; }

    /// <summary>
    /// The hosting account this sign-in currently speaks for: the account the access token's account
    /// claim is issued from, and the one every tenant query follows. Null for an administrator, who
    /// owns none, and for a customer with no membership. Always one the user holds a membership of
    /// (<see cref="AccountMembership"/>); a rotation carries it forward unchanged.
    /// </summary>
    public Guid? ActiveAccountId { get; private set; }

    /// <summary>
    /// Who is behind this session when an operator is viewing the panel as <see cref="UserId"/>
    /// (<see cref="StartViewAs"/>), or <c>null</c> for the user's own sign-in, which has no actor but
    /// its user. The operator's ids exist only inside this value, so an own session cannot carry
    /// them and a view-as session cannot lack them.
    /// </summary>
    public ViewAsOrigin? ViewAs { get; private set; }

    /// <summary>Creates an active session.</summary>
    /// <param name="id">The session's identity; also the <c>sid</c> claim of the access token issued beside it.</param>
    /// <param name="userId">The user this session signs in.</param>
    /// <param name="familyId">
    /// The chain this session belongs to. Every rotation keeps the same family, so detecting a
    /// replayed token can revoke the whole lineage rather than only the link presented.
    /// </param>
    /// <param name="tokenHash">SHA-256 of the refresh token. The token itself is never stored.</param>
    /// <param name="issuedAt">The instant the session was created.</param>
    /// <param name="expiresAt">The instant the refresh token stops being accepted.</param>
    /// <param name="ipAddress">The address the session was created from, for the sessions screen.</param>
    /// <param name="userAgent">The user agent the session was created from, for the sessions screen.</param>
    /// <param name="activeAccountId">The account the sign-in starts out speaking for; null when it has none.</param>
    public Session(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string ipAddress,
        string userAgent,
        Guid? activeAccountId = null)
    {
        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        ActiveAccountId = activeAccountId;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Session()
    {
        TokenHash = string.Empty;
        IpAddress = string.Empty;
        UserAgent = string.Empty;
    }

    /// <summary>Starts a session in which an operator views the panel as another user.</summary>
    /// <param name="id">The session's identity; also the <c>sid</c> claim of the view-as access token.</param>
    /// <param name="viewedUserId">The login being viewed as.</param>
    /// <param name="viewedAccountId">The account the session speaks for: the one the operator chose.</param>
    /// <param name="actorUserId">The operator viewing.</param>
    /// <param name="actorSessionId">The operator's own session, from their access token's <c>sid</c>.</param>
    /// <param name="tokenHash">
    /// SHA-256 of a refresh token that is generated and discarded: the column is required and unique,
    /// and the plaintext never leaves the server, so this session can never be rotated.
    /// </param>
    /// <param name="issuedAt">The instant the session was created.</param>
    /// <param name="ipAddress">The operator's address.</param>
    /// <param name="userAgent">The operator's user agent.</param>
    /// <returns>A live view-as session expiring <see cref="ViewAsLifetime.Duration"/> after <paramref name="issuedAt"/>.</returns>
    public static Session StartViewAs(
        Guid id,
        Guid viewedUserId,
        Guid viewedAccountId,
        Guid actorUserId,
        Guid actorSessionId,
        string tokenHash,
        DateTimeOffset issuedAt,
        string ipAddress,
        string userAgent)
    {
        // A family of its own: a view-as session is never a link in anybody's rotation chain, so
        // reuse detection on the viewed user's own family can never reach it and vice versa.
        var session = new Session(
            id,
            viewedUserId,
            Guid.NewGuid(),
            tokenHash,
            issuedAt,
            issuedAt.Add(ViewAsLifetime.Duration),
            ipAddress,
            userAgent,
            viewedAccountId)
        {
            ViewAs = new ViewAsOrigin(actorUserId, actorSessionId),
        };

        return session;
    }

    /// <summary>Reports whether the session may still be used.</summary>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <returns>True when the session is neither revoked nor expired.</returns>
    public bool IsActive(DateTimeOffset now)
    {
        return Revocation is null && ExpiresAt > now;
    }

    /// <summary>Makes the sign-in speak for another account the login holds a membership of.</summary>
    /// <param name="accountId">
    /// The account to act for, or null when the login no longer holds any. The caller has already
    /// established the membership; this entity does not know the memberships and cannot check it.
    /// </param>
    public void SwitchAccount(Guid? accountId)
    {
        ActiveAccountId = accountId;
    }

    /// <summary>
    /// Revokes the session. A no-op when it is already revoked, so the first reason survives: the
    /// truth worth keeping is why the session originally ended, not what the last caller wanted.
    /// </summary>
    /// <param name="at">The instant of the revocation, taken from <see cref="IClock"/>.</param>
    /// <param name="reason">Why the session is ending.</param>
    public void Revoke(DateTimeOffset at, SessionRevocationReason reason)
    {
        if (Revocation is not null)
        {
            return;
        }

        Revocation = new SessionRevocation(at, reason);
    }
}
