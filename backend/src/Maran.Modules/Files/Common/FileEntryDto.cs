using Maran.Modules.Files.Domain.Enums;

namespace Maran.Modules.Files.Common;

/// <summary>One row of a folder listing or a search.</summary>
/// <param name="Name">The entry's own name, one path segment.</param>
/// <param name="Path">The entry's path relative to the account's home, which is how later requests name it.</param>
/// <param name="Kind">Whether the entry is a file, a folder or something the manager cannot open.</param>
/// <param name="KindName">The kind named in the caller's language.</param>
/// <param name="SizeBytes">Size in bytes; for a folder the size of the directory node itself.</param>
/// <param name="ModifiedAt">When the entry was last modified.</param>
/// <param name="Mode">The nine permission bits as three octal digits, for example <c>644</c>.</param>
/// <param name="IsSymlink">Whether the entry is a symbolic link; it is shown, never followed out of the home.</param>
public sealed record FileEntryDto(
    string Name,
    string Path,
    FileEntryKind Kind,
    string KindName,
    ulong SizeBytes,
    DateTimeOffset ModifiedAt,
    string Mode,
    bool IsSymlink);
