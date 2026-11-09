using Maran.Agent.Client.Services.FilesService;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>The arguments of one directory listing the panel asked the agent for.</summary>
/// <param name="Path">The directory listed.</param>
/// <param name="Offset">Entries skipped.</param>
/// <param name="Limit">Most entries asked for.</param>
/// <param name="IncludeHidden">Whether hidden entries were included.</param>
/// <param name="Sort">The order chosen.</param>
/// <param name="Descending">Whether the order was reversed.</param>
public sealed record AgentListRequest(
    string Path,
    uint Offset,
    uint Limit,
    bool IncludeHidden,
    DirectorySortOrder Sort,
    bool Descending);
