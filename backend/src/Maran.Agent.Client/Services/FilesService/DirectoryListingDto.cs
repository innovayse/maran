namespace Maran.Agent.Client.Services.FilesService;

/// <summary>One page of a directory's entries.</summary>
/// <param name="Entries">The requested page, in the order asked for.</param>
/// <param name="TotalEntries">
/// Entries in the directory after the hidden filter and before paging, so a pager can be drawn. The
/// entries counted in <paramref name="SkippedEntries"/> are not in it.
/// </param>
/// <param name="SkippedEntries">
/// Names left out because they cannot be represented (not UTF-8, or holding a control character).
/// Non-zero means the directory holds entries the panel cannot address.
/// </param>
public sealed record DirectoryListingDto(IReadOnlyList<FileEntryDto> Entries, uint TotalEntries, uint SkippedEntries);
