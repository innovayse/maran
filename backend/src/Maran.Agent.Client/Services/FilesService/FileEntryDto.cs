namespace Maran.Agent.Client.Services.FilesService;

/// <summary>One entry of a directory, as the agent describes it.</summary>
/// <param name="Name">The entry's name only, with no path component.</param>
/// <param name="Kind">What the entry is.</param>
/// <param name="SizeBytes">Size in bytes; zero for a directory.</param>
/// <param name="Mode">The Unix permission bits, e.g. <c>0644</c> as octal.</param>
/// <param name="ModifiedAt">When the entry was last modified, in UTC.</param>
/// <param name="OwnerId">The owning user id of the entry itself, not of a link's target.</param>
/// <param name="GroupId">The owning group id of the entry.</param>
/// <param name="LinkCount">How many hard links the entry has; a file with more than one cannot be read.</param>
public sealed record FileEntryDto(
    string Name,
    FileEntryKind Kind,
    ulong SizeBytes,
    uint Mode,
    DateTimeOffset ModifiedAt,
    uint OwnerId,
    uint GroupId,
    ulong LinkCount);
