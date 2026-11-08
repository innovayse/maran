using Maran.Modules.Files.Domain.Enums;

namespace Maran.Modules.Files.Common;

/// <summary>One entry in full, for the properties dialog.</summary>
/// <param name="Name">The entry's own name, one path segment.</param>
/// <param name="Path">The entry's path relative to the account's home.</param>
/// <param name="Kind">Whether the entry is a file, a folder or something the manager cannot open.</param>
/// <param name="KindName">The kind named in the caller's language.</param>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="ModifiedAt">When the entry was last modified.</param>
/// <param name="Mode">The nine permission bits as three octal digits.</param>
/// <param name="IsSymlink">Whether the entry is a symbolic link.</param>
/// <param name="Owner">The owning user's numeric id as text (the agent reports ids, not names), read-only.</param>
/// <param name="Group">The owning group's numeric id as text, read-only.</param>
public sealed record FileInfoDto(
    string Name,
    string Path,
    FileEntryKind Kind,
    string KindName,
    ulong SizeBytes,
    DateTimeOffset ModifiedAt,
    string Mode,
    bool IsSymlink,
    string Owner,
    string Group);
