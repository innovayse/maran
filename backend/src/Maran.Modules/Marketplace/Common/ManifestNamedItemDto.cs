namespace Maran.Modules.Marketplace.Common;

/// <summary>One thing a module declares it needs, with the name an administrator reads.</summary>
/// <param name="Key">The machine key.</param>
/// <param name="DisplayName">The name in the caller's language.</param>
public sealed record ManifestNamedItemDto(string Key, string DisplayName);
