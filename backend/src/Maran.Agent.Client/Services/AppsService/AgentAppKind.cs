namespace Maran.Agent.Client.Services.AppsService;

/// <summary>What an installed or installable app is for, as the agent reports it.</summary>
/// <remarks>
/// Not named after the wire enum: the generated types may not leave this project, and the panel's own
/// mirror says the one thing the wire cannot, which is "the agent did not state it" as its own member
/// rather than as a zero a reader might take for a real kind.
/// </remarks>
public enum AgentAppKind
{
    /// <summary>The agent did not state the kind; a reader offers no action for it.</summary>
    Unknown = 0,

    /// <summary>The process that owns ports 80 and 443 for hosted sites: nginx or Apache.</summary>
    WebServer = 1,

    /// <summary>A language runtime sites run on: a PHP version or a Node.js major version.</summary>
    Runtime = 2,
}
