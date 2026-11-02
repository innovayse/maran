namespace Maran.Modules.Identity.Common;

/// <summary>One page the caller pinned.</summary>
/// <param name="ItemKey">The navigation tree key of the page.</param>
/// <param name="Position">Where the pin sits in the caller's list, lowest first.</param>
public sealed record PinnedPageDto(string ItemKey, int Position);
