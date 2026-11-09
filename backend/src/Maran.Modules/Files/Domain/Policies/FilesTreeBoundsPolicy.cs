namespace Maran.Modules.Files.Domain.Policies;

/// <summary>
/// The bounds the panel asks the agent to hold a copy or an archive of a tree to: the agent's own
/// ceilings, so the panel never sets the limit lower than the agent would and never above it.
/// </summary>
/// <remarks>
/// The agent refuses a bound above its ceiling and measures the tree before it writes anything, so a
/// tree over the bound is refused as a whole (<c>AgentLimitReached</c>) and leaves nothing behind. The
/// extraction bounds are a different question, a hostile archive's, and are <c>FilesOptions</c>.
/// </remarks>
public static class FilesTreeBoundsPolicy
{
    /// <summary>The most entries (files and folders, the root included) one copy or archive may hold.</summary>
    public const ulong MaximumEntries = 1_000_000;

    /// <summary>The most bytes of file content one copy or archive may hold. 20 GiB.</summary>
    public const ulong MaximumBytes = 20UL * 1024 * 1024 * 1024;
}
