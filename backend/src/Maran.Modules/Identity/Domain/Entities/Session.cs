using Maran.Modules.Identity.Domain.Enums;
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

    /// <summary>The instant the session was revoked; null while it is still live.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Why the session was revoked; null while it is still live.</summary>
    public SessionRevocationReason? RevocationReason { get; private set; }

    /// <summary>The address the session was created from.</summary>
    public string IpAddress { get; private set; }

    /// <summary>The user agent the session was created from.</summary>
    public string UserAgent { get; private set; }

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
    public Session(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string ipAddress,
        string userAgent)
    {
        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Session()
    {
        TokenHash = string.Empty;
        IpAddress = string.Empty;
        UserAgent = string.Empty;
    }

    /// <summary>Reports whether the session may still be used.</summary>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <returns>True when the session is neither revoked nor expired.</returns>
    public bool IsActive(DateTimeOffset now)
    {
        return RevokedAt is null && ExpiresAt > now;
    }

    /// <summary>
    /// Revokes the session. A no-op when it is already revoked, so the first reason survives: the
    /// truth worth keeping is why the session originally ended, not what the last caller wanted.
    /// </summary>
    /// <param name="at">The instant of the revocation, taken from <see cref="IClock"/>.</param>
    /// <param name="reason">Why the session is ending.</param>
    public void Revoke(DateTimeOffset at, SessionRevocationReason reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = at;
        RevocationReason = reason;
    }
}
