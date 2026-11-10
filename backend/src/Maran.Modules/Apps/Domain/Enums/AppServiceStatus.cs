namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>What the service manager reports about an app's service, probed at the time of the read.</summary>
public enum AppServiceStatus
{
    /// <summary>The agent did not state it; "not known" is not "not running".</summary>
    Unknown = 0,

    /// <summary>The service is running.</summary>
    Active = 1,

    /// <summary>The service is installed and not running.</summary>
    Inactive = 2,

    /// <summary>The service is in a failed state.</summary>
    Failed = 3,

    /// <summary>The app has no service, or is not on the host: a Node.js runtime is an interpreter.</summary>
    None = 4,
}
