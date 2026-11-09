namespace Maran.Agent.Client.Services.FilesService;

/// <summary>One entry of an account's trash, as the agent lists it.</summary>
/// <param name="TrashId">The entry's identifier in the trash; the only handle a restore accepts.</param>
/// <param name="OriginalPath">
/// Where the entry lived when it was trashed, relative to the account home. Read from a file the
/// customer can edit, so it is shown and never trusted.
/// </param>
/// <param name="TrashedAt">When the entry was trashed, in UTC.</param>
/// <param name="IsDirectory">Whether the trashed entry is a real directory.</param>
/// <param name="SizeBytes">Size of a trashed file in bytes; zero for a directory.</param>
public sealed record TrashedEntryDto(
    string TrashId,
    string OriginalPath,
    DateTimeOffset TrashedAt,
    bool IsDirectory,
    ulong SizeBytes);
