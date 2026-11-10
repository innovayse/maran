namespace Maran.Agent.Client.Services.AppsService;

/// <summary>What the service manager reports about an app's service.</summary>
public enum AgentAppServiceStatus
{
    /// <summary>The agent did not state it; "not known" is not "not running".</summary>
    Unknown = 0,

    /// <summary>The service is running.</summary>
    Active = 1,

    /// <summary>The service is installed and not running.</summary>
    Inactive = 2,

    /// <summary>The service is in a failed state.</summary>
    Failed = 3,

    /// <summary>The app has no service: a Node.js runtime is an interpreter, and a site's own unit is the thing that runs.</summary>
    None = 4,
}
