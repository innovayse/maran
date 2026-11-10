namespace Maran.Agent.Client.Services.AppsService;

/// <summary>Which branch of an install or uninstall response a message carried.</summary>
internal enum AppStreamBranch
{
    /// <summary>The message carried no branch at all: neither progress nor an outcome.</summary>
    Empty = 0,

    /// <summary>Progress while the operation runs.</summary>
    Progress = 1,

    /// <summary>The agent's terminal success.</summary>
    Completed = 2,

    /// <summary>The agent's terminal failure.</summary>
    Error = 3,
}
