namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>The bounds a Node site's listening port and start command must stay inside.</summary>
/// <remarks>
/// The panel refusing early; the agent validates both again on its own side, and it is the agent that
/// decides what a start command may contain before it reaches a unit file (rules/security.md).
/// </remarks>
public static class NodeAppLimits
{
    /// <summary>The lowest port a site's application may listen on: the first unprivileged one.</summary>
    public const int MinimumPort = 1024;

    /// <summary>The highest port a site's application may listen on.</summary>
    public const int MaximumPort = 65535;

    /// <summary>The longest start command, in characters.</summary>
    public const int MaximumStartCommandLength = 1024;

    /// <summary>Whether a start command is one printable line with something in it.</summary>
    /// <param name="command">The command as submitted.</param>
    /// <returns>False for an empty or whitespace-only command, and for any control character, newline included.</returns>
    public static bool IsStartCommand(string command)
    {
        return !string.IsNullOrWhiteSpace(command)
            && command.Length <= MaximumStartCommandLength
            && !command.Any(char.IsControl);
    }
}
