namespace Maran.Agent.Client.Services.PhpService;

/// <summary>
/// Whether the agent reported an installed PHP runtime as the host's default CLI PHP.
/// </summary>
/// <remarks>
/// The wire field <c>is_default</c> has explicit presence, and the agent does not currently
/// establish which version is the default, so it leaves the field unset. Three answers therefore
/// exist and none may stand in for another: "not known" is not "not the default".
/// </remarks>
public enum AgentPhpDefaultStatus
{
    /// <summary>The agent reported this version as the host's default CLI PHP.</summary>
    Default = 0,

    /// <summary>The agent reported this version as installed but not the host's default.</summary>
    NotDefault = 1,

    /// <summary>The agent did not establish whether this version is the host's default.</summary>
    Unknown = 2,
}
