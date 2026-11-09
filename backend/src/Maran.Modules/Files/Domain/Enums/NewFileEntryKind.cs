namespace Maran.Modules.Files.Domain.Enums;

/// <summary>What a customer may ask to be created: an empty file or a folder, and nothing else.</summary>
/// <remarks>
/// Its own type rather than <see cref="FileEntryKind"/> with a value refused, because a link or a node
/// is something that can be listed and can never be created from the manager.
/// </remarks>
public enum NewFileEntryKind
{
    /// <summary>An empty regular file.</summary>
    File,

    /// <summary>An empty folder.</summary>
    Directory,
}
