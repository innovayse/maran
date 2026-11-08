namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>What the panel can do with one catalogue PHP extension on this host's OS family.</summary>
/// <remarks>
/// The panel's own mirror of the agent client's availability, so a handler and a DTO never carry the
/// agent client's type outward. <see cref="Unknown"/> offers no action; it is a different answer from
/// <see cref="NotPackaged"/>, which says the family has no package to install.
/// </remarks>
public enum ExtensionAvailability
{
    /// <summary>The agent did not state what can be done with the extension; offer no action.</summary>
    Unknown = 0,

    /// <summary>The family has a package and a switch for both directions: it can be turned on and off.</summary>
    Available = 1,

    /// <summary>The family has a package but no clean switch: it can be turned on, never off.</summary>
    DisableNotSupported = 2,

    /// <summary>The family has no separate package for it, so it cannot be turned on.</summary>
    NotPackaged = 3,
}
