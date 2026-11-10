namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>
/// Where a catalogue module stands on this panel. A machine value the screen branches on, decided in one
/// place (<c>InstallEligibility</c>) and sent as a camelCase word.
/// </summary>
public enum MarketplaceModuleState
{
    /// <summary>Offered and not installed.</summary>
    Available,

    /// <summary>Installed at the newest version the catalogue offers, or at a newer one.</summary>
    Installed,

    /// <summary>Installed, and the catalogue offers a newer version.</summary>
    UpdateAvailable,

    /// <summary>The service has withdrawn the module or its newest version.</summary>
    Revoked,

    /// <summary>The newest version does not run on this panel's version.</summary>
    Incompatible,
}
