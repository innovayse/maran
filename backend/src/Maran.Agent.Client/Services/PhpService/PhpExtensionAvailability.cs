namespace Maran.Agent.Client.Services.PhpService;

/// <summary>What the panel can do with one catalogue PHP extension on this host's OS family.</summary>
/// <remarks>
/// <see cref="Unknown"/> is the panel's own answer for a reply that states nothing, and it is a
/// different answer from every other member: it offers no action, where <see cref="NotPackaged"/> says
/// the family has no package to install. The wire's unset value is read as <see cref="Unknown"/> once,
/// in <c>PhpExtensionMapper</c>, so the panel never carries the wire's zero as if it were a statement.
/// </remarks>
public enum PhpExtensionAvailability
{
    /// <summary>The agent did not state what can be done with the extension; offer no action.</summary>
    Unknown = 0,

    /// <summary>The family has a package and a switch for both directions: it can be turned on and off.</summary>
    Available = 1,

    /// <summary>The family has a package but no clean switch: it can be turned on, never off.</summary>
    DisableNotSupported = 2,

    /// <summary>The family has no separate package for it, so the agent cannot turn it on.</summary>
    NotPackaged = 3,
}
