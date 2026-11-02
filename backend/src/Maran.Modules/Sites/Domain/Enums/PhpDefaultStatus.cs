namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>Whether an installed PHP runtime is the host's default CLI PHP, as far as the host reported.</summary>
/// <remarks>
/// Three answers, because the agent can decline to establish the default: "not known" is a fact an
/// operator is shown, and it must never be rendered as "not the default".
/// </remarks>
public enum PhpDefaultStatus
{
    /// <summary>This version is the host's default CLI PHP.</summary>
    Default = 0,

    /// <summary>This version is installed and is not the host's default CLI PHP.</summary>
    NotDefault = 1,

    /// <summary>The host did not establish whether this version is the default.</summary>
    Unknown = 2,
}
