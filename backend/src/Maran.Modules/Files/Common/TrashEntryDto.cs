using Maran.Modules.Files.Domain.Enums;

namespace Maran.Modules.Files.Common;

/// <summary>One row of the trash. A trashed entry is addressed by its id, never by a path.</summary>
/// <param name="Id">The identity a restore names the entry by.</param>
/// <param name="Name">The entry's name, one path segment.</param>
/// <param name="OriginalPath">Where the entry lived before it was trashed, relative to the home.</param>
/// <param name="Kind">Whether it was a file, a folder or something else.</param>
/// <param name="KindName">The kind named in the caller's language.</param>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="DeletedAt">When it was moved to the trash.</param>
/// <param name="ExpiresAt">When the retention job will remove it for good.</param>
public sealed record TrashEntryDto(
    string Id,
    string Name,
    string OriginalPath,
    FileEntryKind Kind,
    string KindName,
    ulong SizeBytes,
    DateTimeOffset DeletedAt,
    DateTimeOffset ExpiresAt);
