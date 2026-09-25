namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One outstanding permission for a newly created hosting account's owner to set their first
/// password. Stored as a digest only, and usable exactly once.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors <see cref="PasswordResetToken"/> — a plaintext token exists only for one request and
/// one e-mail, only its digest (see <c>InvitationTokenHasher</c>) is persisted, and <see cref="IsUsable"/>
/// checks expiry and use together so no caller can honour one while forgetting the other. Two
/// differences from <see cref="PasswordResetToken"/> are deliberate:
/// </para>
/// <para>
/// <b>The lifetime is a week, not an hour.</b> An invitation is the first contact a new customer has
/// with the panel — unlike a password reset, which the user themselves triggered moments earlier, an
/// invitation mail may sit unread in an inbox over a weekend before anyone looks at it. See
/// <see cref="Lifetime"/>.
/// </para>
/// <para>
/// <b>Single use is recorded, not deleted, for the same reason it is on <see cref="PasswordResetToken"/>.</b>
/// <see cref="UsedAt"/> is stamped rather than the row being removed: a deleted row is
/// indistinguishable from one that never existed, so a replayed invitation token would read as "no
/// such token" instead of the interesting journal entry it actually is.
/// </para>
/// </remarks>
public sealed class InvitationToken
{
    /// <summary>How long a token stays valid.</summary>
    /// <remarks>
    /// Seven days: an invitation is the first contact with a new customer, and the mail carrying it
    /// may sit unread for a while before anyone acts on it — much longer than the hour a
    /// self-triggered password reset is given.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>The token's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user, in <c>UserState.Invited</c>, whose password this token may set.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The base64-encoded SHA-256 digest of the token. Never the token itself.</summary>
    public string TokenHash { get; private set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the token was spent, or <c>null</c> while it is still unused.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Issues an invitation token for one newly created user.</summary>
    /// <param name="id">The token's identity.</param>
    /// <param name="userId">The invited user whose password it may set.</param>
    /// <param name="tokenHash">The digest of the generated token, from <c>InvitationTokenHasher</c>.</param>
    /// <param name="createdAt">The instant of issue, taken from <see cref="IClock"/>.</param>
    public InvitationToken(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private InvitationToken()
    {
        TokenHash = string.Empty;
    }

    /// <summary>Whether the token may still be spent at <paramref name="now"/>.</summary>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <returns><c>true</c> only when it is unspent and unexpired.</returns>
    public bool IsUsable(DateTimeOffset now)
    {
        return UsedAt is null && ExpiresAt > now;
    }

    /// <summary>Spends the token, so it can never be spent again.</summary>
    /// <param name="at">The instant it was spent, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// The first spend survives, like every other one-way transition in this module: a second call
    /// is a no-op rather than a re-stamp, so the journal's timestamp remains the moment the password
    /// actually changed.
    /// </remarks>
    public void Consume(DateTimeOffset at)
    {
        if (UsedAt is not null)
        {
            return;
        }

        UsedAt = at;
    }
}
