namespace Maran.Agent.Simulated.State;

/// <summary>One file or directory under an account's home on the simulated server.</summary>
/// <param name="IsDirectory">Whether the entry is a directory; a directory holds no content of its own.</param>
/// <param name="Content">
/// The file's content as text, stored as data and never interpreted or served; empty for a directory.
/// </param>
/// <param name="Mode">The permission bits, masked to the twelve a mode can carry.</param>
public sealed record SimulatedFile(bool IsDirectory, string Content, uint Mode);
