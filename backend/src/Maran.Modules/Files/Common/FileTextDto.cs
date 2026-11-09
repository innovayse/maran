namespace Maran.Modules.Files.Common;

/// <summary>A file small and plain enough to edit.</summary>
/// <param name="Content">The file's text, to be rendered as text and never as markup.</param>
/// <param name="ModifiedAt">When the file was last modified; sent back on save so a stale tab is caught.</param>
/// <param name="Mode">The file's permission bits as octal digits, which a save keeps.</param>
/// <param name="SizeBytes">The file's size in bytes.</param>
public sealed record FileTextDto(string Content, DateTimeOffset ModifiedAt, string Mode, ulong SizeBytes);
