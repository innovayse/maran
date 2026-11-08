namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>The arguments of one search the panel asked the agent for.</summary>
/// <param name="Path">The directory searched from.</param>
/// <param name="Query">The substring asked for.</param>
/// <param name="MaxDepth">The deepest level asked for.</param>
/// <param name="MaxResults">The most matches asked for.</param>
/// <param name="IncludeHidden">Whether hidden entries were included.</param>
public sealed record AgentSearchRequest(string Path, string Query, uint MaxDepth, uint MaxResults, bool IncludeHidden);
