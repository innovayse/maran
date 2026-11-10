namespace Maran.Modules.Apps.Domain.Enums;

/// <summary>What an administrator asks to be done to an installed app's service.</summary>
public enum AppServiceAction
{
    /// <summary>Starts the service.</summary>
    Start = 1,

    /// <summary>Stops the service. Stopping a web server takes every site it serves off the air.</summary>
    Stop = 2,

    /// <summary>Stops and starts the service.</summary>
    Restart = 3,

    /// <summary>Asks the running service to re-read its configuration without dropping connections.</summary>
    Reload = 4,
}
