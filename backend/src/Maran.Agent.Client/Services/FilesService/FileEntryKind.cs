namespace Maran.Agent.Client.Services.FilesService;

/// <summary>What a directory entry is, as far as the agent's listing says.</summary>
/// <remarks>
/// Three values because the wire states two facts, a real directory and a symbolic link, and the
/// listing never follows a link: a link to a directory is a <see cref="Symlink"/> and not a
/// <see cref="Directory"/>, so no caller descends into one by reading a boolean.
/// </remarks>
public enum FileEntryKind
{
    /// <summary>
    /// Anything that is neither a real directory nor a link. A FIFO, device or socket is listed here
    /// too, because the wire does not tell them apart from a regular file; opening one is refused by
    /// the agent, never attempted.
    /// </summary>
    File,

    /// <summary>A real directory. A link to a directory is <see cref="Symlink"/>, not this.</summary>
    Directory,

    /// <summary>A symbolic link; its size, mode and time are the link's own, never its target's.</summary>
    Symlink,
}
