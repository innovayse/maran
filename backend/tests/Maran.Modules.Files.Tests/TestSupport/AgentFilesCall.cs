namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>One call the panel made to the agent's files client, as the fake saw it.</summary>
/// <param name="Operation">The client method's name, e.g. <c>MoveEntryAsync</c>.</param>
/// <param name="Account">The system user name the call was addressed to.</param>
/// <param name="Path">The primary path of the call, or an empty string for an account-wide operation.</param>
/// <param name="Destination">The second path of a move or a copy, otherwise <see langword="null"/>.</param>
/// <param name="Mode">The permission bits the call carried, otherwise <see langword="null"/>.</param>
public sealed record AgentFilesCall(
    string Operation,
    string Account,
    string Path,
    string? Destination = null,
    uint? Mode = null);
