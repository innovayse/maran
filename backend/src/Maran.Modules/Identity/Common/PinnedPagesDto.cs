namespace Maran.Modules.Identity.Common;

/// <summary>The caller's pinned pages, with how many they may keep.</summary>
/// <param name="Items">The pins, lowest position first.</param>
/// <param name="Limit">The most pages one user may pin; the SPA reads it instead of holding its own.</param>
public sealed record PinnedPagesDto(IReadOnlyList<PinnedPageDto> Items, int Limit);
