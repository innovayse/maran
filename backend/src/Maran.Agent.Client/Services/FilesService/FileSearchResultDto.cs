namespace Maran.Agent.Client.Services.FilesService;

/// <summary>What a name search found.</summary>
/// <param name="Matches">The matching entries, in the order the search met them.</param>
/// <param name="Truncated">
/// True when a bound (depth, result count or entries examined) stopped the search, so more entries
/// may match than were returned. A truncated search is a success, never an error.
/// </param>
/// <param name="SkippedEntries">Names left out because they cannot be represented.</param>
public sealed record FileSearchResultDto(IReadOnlyList<FileSearchMatchDto> Matches, bool Truncated, uint SkippedEntries);
