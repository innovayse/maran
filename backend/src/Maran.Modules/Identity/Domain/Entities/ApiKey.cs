using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// A long-lived credential an administrator hands to an integration, which then calls the endpoints
/// that opt into key authentication without signing in. Only the SHA-256 digest of the key is stored:
/// the plaintext exists once, in the response that created it. A key is panel-level administrator data,
/// not an account's row, so it carries no account id and no tenant filter applies to it.
/// </summary>
/// <remarks>
/// The creator is recorded as a plain id and is deliberately not a foreign key: deleting a login must
/// not silently delete a credential an integration depends on, and revoking it is an administrator's
/// explicit act. Scopes and address ranges are fixed at creation; rotation is issuing a new key and
/// revoking the old one, so nothing here widens an existing key.
/// </remarks>
public sealed class ApiKey
{
    /// <summary>The key's identity; also what authenticated calls and audit entries name it by.</summary>
    public Guid Id { get; private set; }

    /// <summary>The administrator's label for the key, at most <see cref="ApiKeyLimits.MaxNameLength"/> characters.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// The key's public prefix, <see cref="ApiKeyLimits.PrefixLength"/> lower-case hexadecimal characters
    /// drawn separately from the secret. An index for lookup and a display handle; it discloses none of the secret.
    /// </summary>
    public string Prefix { get; private set; }

    /// <summary>The SHA-256 digest of the whole key as presented. The key itself is never stored.</summary>
    public string TokenHash { get; private set; }

    /// <summary>
    /// The scopes the key grants, each a member of <see cref="ApiKeyScopes.All"/>, without duplicates.
    /// Never mutated after creation: callers must treat the array as read-only.
    /// </summary>
    public string[] Scopes { get; private set; }

    /// <summary>
    /// The normalised address ranges the key may be used from, or <c>null</c> when it is not bound to
    /// any. Never mutated after creation: callers must treat the array as read-only.
    /// </summary>
    public string[]? AllowedCidrs { get; private set; }

    /// <summary>The administrator who issued the key.</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>The instant the key was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The instant the key stops authenticating, or <c>null</c> for a key that lives until revoked.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The instant of the last recorded use; accurate to <see cref="ApiKeyLimits.LastUseRecordingInterval"/>.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>The address of the call that <see cref="LastUsedAt"/> records.</summary>
    public string? LastUsedIp { get; private set; }

    /// <summary>The instant an administrator revoked the key, or <c>null</c> while it has not been.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ApiKey()
    {
        Name = string.Empty;
        Prefix = string.Empty;
        TokenHash = string.Empty;
        Scopes = [];
    }

    /// <summary>Issues a new key row, validating every input against <see cref="ApiKeyLimits"/> and the scope vocabulary.</summary>
    /// <param name="id">The key's identity.</param>
    /// <param name="name">The label; trimmed, then 1 to <see cref="ApiKeyLimits.MaxNameLength"/> characters.</param>
    /// <param name="prefix">The public prefix: exactly <see cref="ApiKeyLimits.PrefixLength"/> lower-case hexadecimal characters.</param>
    /// <param name="tokenHash">The digest of the key; non-empty and at most <see cref="ApiKeyLimits.MaxDigestLength"/> characters.</param>
    /// <param name="scopes">The scopes to grant: members of <see cref="ApiKeyScopes.All"/>, without duplicates. May be empty.</param>
    /// <param name="allowedCidrs">
    /// The already-normalised address ranges the key is bound to: <c>null</c> for none, otherwise 1 to
    /// <see cref="ApiKeyLimits.MaxCidrs"/> distinct non-blank entries. Their syntax is the caller's to have checked.
    /// </param>
    /// <param name="createdByUserId">The administrator issuing the key.</param>
    /// <param name="createdAt">The instant of issue, taken from <see cref="IClock"/>.</param>
    /// <param name="expiresAt">The expiry instant, which must be after <paramref name="createdAt"/>, or <c>null</c> for none.</param>
    /// <returns>The new, active key.</returns>
    /// <exception cref="ArgumentException">An input breaks a limit or names a scope outside the vocabulary.</exception>
    public static ApiKey Issue(
        Guid id,
        string name,
        string prefix,
        string tokenHash,
        IReadOnlyCollection<string> scopes,
        IReadOnlyCollection<string>? allowedCidrs,
        Guid createdByUserId,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0 || trimmedName.Length > ApiKeyLimits.MaxNameLength)
        {
            throw new ArgumentException("A name is 1 to 64 characters.", nameof(name));
        }

        if (!IsValidPrefix(prefix))
        {
            throw new ArgumentException("A prefix is 8 lower-case hexadecimal characters.", nameof(prefix));
        }

        if (string.IsNullOrEmpty(tokenHash) || tokenHash.Length > ApiKeyLimits.MaxDigestLength)
        {
            throw new ArgumentException("A digest is 1 to 64 characters.", nameof(tokenHash));
        }

        if (scopes is null
            || scopes.Any(scope => !ApiKeyScopes.All.Contains(scope))
            || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Count)
        {
            throw new ArgumentException("Scopes must be distinct members of the scope vocabulary.", nameof(scopes));
        }

        if (allowedCidrs is not null
            && (allowedCidrs.Count == 0
                || allowedCidrs.Count > ApiKeyLimits.MaxCidrs
                || allowedCidrs.Any(cidr => string.IsNullOrWhiteSpace(cidr))
                || allowedCidrs.Distinct(StringComparer.Ordinal).Count() != allowedCidrs.Count))
        {
            throw new ArgumentException("Address ranges are 1 to 16 distinct, non-blank entries.", nameof(allowedCidrs));
        }

        if (expiresAt is not null && expiresAt <= createdAt)
        {
            throw new ArgumentException("An expiry must be after the issue instant.", nameof(expiresAt));
        }

        return new ApiKey
        {
            Id = id,
            Name = trimmedName,
            Prefix = prefix,
            TokenHash = tokenHash,
            Scopes = scopes.ToArray(),
            AllowedCidrs = allowedCidrs?.ToArray(),
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>
    /// Revokes the key. Idempotent: a key revoked already keeps its first revocation instant, so a
    /// repeated or racing revoke cannot move the time an audit reader relies on.
    /// </summary>
    /// <param name="at">The instant of revocation, taken from <see cref="IClock"/>.</param>
    public void Revoke(DateTimeOffset at)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = at;
    }

    /// <summary>
    /// Notes a successful use, unless the last one was recorded less than
    /// <see cref="ApiKeyLimits.LastUseRecordingInterval"/> ago, so a busy key does not write its row on every call.
    /// A use exactly one interval after the last is recorded.
    /// </summary>
    /// <param name="at">The instant of the call.</param>
    /// <param name="ipAddress">The caller's address, as spelled by <c>ClientAddress</c>.</param>
    /// <returns><c>true</c> when the row changed and must be saved; <c>false</c> when the use fell inside the throttle window.</returns>
    public bool RecordUse(DateTimeOffset at, string ipAddress)
    {
        if (LastUsedAt is not null && at - LastUsedAt.Value < ApiKeyLimits.LastUseRecordingInterval)
        {
            return false;
        }

        LastUsedAt = at;
        LastUsedIp = ipAddress.Length > ApiKeyLimits.MaxAddressLength
            ? ipAddress[..ApiKeyLimits.MaxAddressLength]
            : ipAddress;
        return true;
    }

    /// <summary>Whether the key may authenticate at <paramref name="now"/>: not revoked and not past its expiry.</summary>
    /// <param name="now">The instant to judge at.</param>
    /// <returns><c>true</c> for an active key.</returns>
    public bool IsUsable(DateTimeOffset now)
    {
        return StatusAt(now) == ApiKeyStatus.Active;
    }

    /// <summary>Derives where the key stands at <paramref name="now"/>; a revoked key is revoked even after its expiry.</summary>
    /// <param name="now">The instant to judge at.</param>
    /// <returns>The derived status.</returns>
    public ApiKeyStatus StatusAt(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return ApiKeyStatus.Revoked;
        }

        if (ExpiresAt is not null && now >= ExpiresAt)
        {
            return ApiKeyStatus.Expired;
        }

        return ApiKeyStatus.Active;
    }

    /// <summary>Whether <paramref name="prefix"/> is exactly the prefix shape: eight lower-case hexadecimal characters.</summary>
    /// <param name="prefix">The candidate.</param>
    /// <returns><c>true</c> when it has the shape.</returns>
    private static bool IsValidPrefix(string? prefix)
    {
        return prefix is { Length: ApiKeyLimits.PrefixLength }
            && prefix.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
