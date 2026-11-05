namespace Maran.Modules.Identity.Common;

/// <summary>
/// What an API key may learn about itself: the answer an integration reads to check which key it
/// holds and what it may do. Carries nothing about who created the key or where it was used from.
/// </summary>
/// <param name="Id">The key's identity.</param>
/// <param name="Name">The label the administrator gave the key.</param>
/// <param name="Prefix">The eight public characters that identify the key; not a credential.</param>
/// <param name="Scopes">The scopes the key holds, <c>self.read</c> included.</param>
/// <param name="ExpiresAt">When the key stops working, or <c>null</c> for a key that lives until revoked.</param>
/// <param name="LastUsedAt">When the key last authenticated a call (accurate to about a minute), or <c>null</c>.</param>
public sealed record ApiKeySelfDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt);
