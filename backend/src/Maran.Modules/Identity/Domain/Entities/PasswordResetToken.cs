namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One outstanding permission to set a new password without knowing the old one. Stored as a digest
/// only, valid for an hour, and usable exactly once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a row at all, when the mail is not persisted.</b> The token itself must be verifiable when
/// the user comes back with it, and there is nothing else to verify it against — the panel is one
/// process with no shared cache. What is stored is the DIGEST (see
/// <c>PasswordResetTokenHasher</c>), so the row is not the secret: a dump of this table lets an
/// attacker recognise a token they already hold, and nothing more.
/// </para>
/// <para>
/// <b>Single use is recorded, not deleted.</b> <see cref="UsedAt"/> is stamped rather than the row
/// being removed, because a token presented twice must be refused rather than met with "no such
/// token" — and a deleted row cannot tell those apart. It makes no difference to the ANSWER the
/// caller gets, which is identical for a spent token, an expired one and one that never existed; it
/// makes the difference to the journal, where a replayed token is the interesting entry.
/// </para>
/// <para>
/// <b>Expiry and use are both checked, in one method.</b> <see cref="IsUsable"/> exists so no caller
/// can check one and forget the other; a reset that honoured a spent token, or an unexpiring one,
/// is a permanent account takeover from a single intercepted mail.
/// </para>
/// </remarks>
public sealed class PasswordResetToken
{
    /// <summary>How long a token stays valid.</summary>
    /// <remarks>
    /// An hour: long enough to survive a mail server's queue and somebody reading their mail after
    /// lunch, short enough that a message sitting in an abandoned inbox stops being a key to the
    /// panel by the end of the afternoon.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>The token's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user whose password this token may set.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The base64-encoded SHA-256 digest of the token. Never the token itself.</summary>
    public string TokenHash { get; private set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the token was spent, or <c>null</c> while it is still unused.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Issues a token for one user.</summary>
    /// <param name="id">The token's identity.</param>
    /// <param name="userId">The user whose password it may set.</param>
    /// <param name="tokenHash">The digest of the generated token, from <c>PasswordResetTokenHasher</c>.</param>
    /// <param name="createdAt">The instant of issue, taken from <see cref="IClock"/>.</param>
    public PasswordResetToken(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private PasswordResetToken()
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
