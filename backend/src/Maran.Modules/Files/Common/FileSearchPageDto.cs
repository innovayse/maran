namespace Maran.Modules.Files.Common;

/// <summary>One page of a search plus whether the agent stopped at one of its ceilings.</summary>
/// <param name="Items">The matches on this page.</param>
/// <param name="Total">How many matches there are in all.</param>
/// <param name="Page">The page asked for.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="Truncated">True when the agent hit a depth or result ceiling, so the matches are not all of them.</param>
public sealed record FileSearchPageDto(
    IReadOnlyList<FileEntryDto> Items,
    int Total,
    int Page,
    int PageSize,
    bool Truncated);
