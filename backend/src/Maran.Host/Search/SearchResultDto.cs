namespace Maran.Host.Search;

/// <summary>
/// The global search's answer: one group per provider that was asked, in a stable order. A provider that
/// was not asked (its module is not enabled, or it answers for no kind the caller can have) has no group.
/// </summary>
/// <param name="Groups">The groups, one per provider asked.</param>
public sealed record SearchResultDto(IReadOnlyList<SearchGroupDto> Groups);
