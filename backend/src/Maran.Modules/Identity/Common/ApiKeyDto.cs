using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// One API key as the key list shows it. Carries the public prefix and the key's limits and use, and
/// has no member the key itself or its digest could occupy, so "the list never leaks a key" is a
/// property of the type rather than a rule someone has to keep.
/// </summary>
/// <param name="Id">The key's identity, used to revoke it.</param>
/// <param name="Name">The label the administrator gave the key.</param>
/// <param name="Prefix">The eight public characters that identify the key in a list; not a credential.</param>
/// <param name="Scopes">The scopes the key was granted.</param>
/// <param name="AllowedCidrs">The address ranges the key may be used from; empty when it is not bound to any.</param>
/// <param name="CreatedAt">When the key was created.</param>
/// <param name="ExpiresAt">When the key stops working, or <c>null</c> for a key that lives until revoked.</param>
/// <param name="LastUsedAt">When the key last authenticated a call (accurate to about a minute), or <c>null</c>.</param>
/// <param name="LastUsedIp">The address of the call <paramref name="LastUsedAt"/> records, or <c>null</c>.</param>
/// <param name="RevokedAt">When the key was revoked, or <c>null</c> while it has not been.</param>
/// <param name="Status">Whether the key works now: active, revoked or expired. Derived at read time, never stored.</param>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AllowedCidrs,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    string? LastUsedIp,
    DateTimeOffset? RevokedAt,
    ApiKeyStatus Status);
