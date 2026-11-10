namespace Maran.Agent.Client.Services.AppsService;

/// <summary>What the panel asks the agent to do to an app's service.</summary>
/// <remarks>
/// No "unspecified" member: the panel never sends one, and the agent refuses it anyway. A closed
/// set of four is the whole of what an administrator can ask.
/// </remarks>
public enum AgentAppServiceAction
{
    /// <summary>Starts the service.</summary>
    Start = 1,

    /// <summary>Stops the service.</summary>
    Stop = 2,

    /// <summary>Stops and starts the service.</summary>
    Restart = 3,

    /// <summary>Asks the running service to re-read its configuration without dropping connections.</summary>
    Reload = 4,
}
