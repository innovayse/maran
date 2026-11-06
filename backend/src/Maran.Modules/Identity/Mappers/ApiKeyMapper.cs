using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Builds the outward shapes of an <see cref="ApiKey"/>. One place, because the list, the create
/// answer and the self answer all describe the same row, and none of them may ever grow a member that
/// carries the digest.
/// </summary>
public static class ApiKeyMapper
{
    /// <summary>Projects a key into the shape the key list shows.</summary>
    /// <param name="key">The key row.</param>
    /// <param name="now">The instant the status is read at.</param>
    /// <param name="statusNames">Names the status in the request's culture; the mapper restates its answer.</param>
    /// <returns>The key as the list shows it.</returns>
    public static ApiKeyDto From(ApiKey key, DateTimeOffset now, ApiKeyStatusDisplayNames statusNames)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(statusNames);

        var status = key.StatusAt(now);

        return new ApiKeyDto(
            key.Id,
            key.Name,
            key.Prefix,
            key.Scopes,
            key.AllowedCidrs ?? [],
            key.CreatedAt,
            key.ExpiresAt,
            key.LastUsedAt,
            key.LastUsedIp,
            key.RevokedAt,
            status,
            statusNames.Of(status));
    }

    /// <summary>Wraps a freshly created key and its plaintext into the one answer that carries it.</summary>
    /// <param name="key">The key row that was just stored.</param>
    /// <param name="secret">The plaintext key, shown once.</param>
    /// <param name="now">The instant the status is read at.</param>
    /// <param name="statusNames">Names the status in the request's culture.</param>
    /// <returns>The create answer.</returns>
    public static ApiKeyCreatedDto Created(ApiKey key, string secret, DateTimeOffset now, ApiKeyStatusDisplayNames statusNames)
    {
        return new ApiKeyCreatedDto(From(key, now, statusNames), new SensitiveString(secret));
    }

    /// <summary>Projects a key into what the key may learn about itself.</summary>
    /// <param name="key">The key row.</param>
    /// <returns>The key's own view.</returns>
    public static ApiKeySelfDto Self(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new ApiKeySelfDto(
            key.Id,
            key.Name,
            key.Prefix,
            ApiKeyScopePolicy.EffectiveOf(key),
            key.ExpiresAt,
            key.LastUsedAt);
    }
}
