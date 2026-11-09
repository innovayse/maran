namespace Maran.Packaging.Models;

/// <summary>
/// An operator's explicit decision to install an OLDER version of a module than the one installed.
/// Going back is how a withdrawn fix is reinstated, so it is never the default: the decision and its
/// reason are both required, and the caller records the reason.
/// </summary>
/// <param name="Reason">Why the downgrade is wanted; blank is refused as if no override were given.</param>
public sealed record IpmDowngradeOverride(string Reason);
