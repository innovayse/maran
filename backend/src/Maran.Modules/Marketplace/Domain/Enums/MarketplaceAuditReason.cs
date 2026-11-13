namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>
/// Why a marketplace request was refused, as a closed set: the only reason vocabulary an audit entry may
/// carry. Each member is spelled exactly like the error code it stands for, so a failure maps to its
/// reason by name and a reason can never hold free text.
/// </summary>
/// <remarks>
/// A closed set is the point. A reason written as a string would let a URL, a grant id or an exception
/// message reach a journal that is append-only and never deleted (rules/security.md item 8); an enum has
/// nowhere to put one.
/// </remarks>
public enum MarketplaceAuditReason
{
    /// <summary>The request was not refused.</summary>
    None,

    /// <summary>The refusal fits none of the named reasons; the module id and version still say what was asked.</summary>
    Unclassified,

    /// <summary>The marketplace is switched off.</summary>
    MarketplaceDisabled,

    /// <summary>The marketplace address is not usable.</summary>
    MarketplaceHostNotConfigured,

    /// <summary>The marketplace could not be reached.</summary>
    MarketplaceUnreachable,

    /// <summary>A catalogue answer carried no valid signature.</summary>
    CatalogueUnsigned,

    /// <summary>A signed catalogue answer was older than the panel accepts.</summary>
    CatalogueStale,

    /// <summary>The pinned verification key is absent or unreadable.</summary>
    CatalogueKeyMissing,

    /// <summary>The action needs a licence lease and the server has none.</summary>
    LeaseRequired,

    /// <summary>The licence does not include the module.</summary>
    EntitlementMissing,

    /// <summary>The version does not work with this panel.</summary>
    IncompatiblePanel,

    /// <summary>The publisher withdrew the version.</summary>
    PackageRevoked,

    /// <summary>The download address was on another host.</summary>
    GrantHostRefused,

    /// <summary>The downloaded package was not the announced size.</summary>
    DownloadSizeMismatch,

    /// <summary>The downloaded package did not match the announced digest.</summary>
    DownloadDigestMismatch,

    /// <summary>The service no longer knows the server's licence.</summary>
    LicenceUnknown,

    /// <summary>The service refused because the licence was revoked.</summary>
    LicenceRevoked,

    /// <summary>The service limited how often the server may ask.</summary>
    MarketplaceRateLimited,

    /// <summary>The service no longer accepted the download address.</summary>
    DownloadGrantUnusable,

    /// <summary>The module is not in the catalogue.</summary>
    ModuleNotInCatalogue,

    /// <summary>The module is not installed.</summary>
    ModuleNotInstalled,

    /// <summary>Another operation on the module was running.</summary>
    ModuleOperationInProgress,

    /// <summary>The version cannot be installed, for example because it is below the module's floor.</summary>
    ModuleVersionNotInstallable,

    /// <summary>The administrator did not acknowledge what the module is allowed to do.</summary>
    InstallNotAcknowledged,

    /// <summary>The administrator's password check failed.</summary>
    ReauthenticationFailed,

    /// <summary>The package did not match what the marketplace announced.</summary>
    ModulePackageNotAsAnnounced,

    /// <summary>The module is already installed at the version asked for.</summary>
    ModuleAlreadyInstalled,

    /// <summary>The module has no earlier version to go back to.</summary>
    ModuleHasNoPreviousVersion,

    /// <summary>The operation stopped before it finished.</summary>
    ModuleOperationFailed,

    /// <summary>The folder for downloaded packages could not be written.</summary>
    MarketplaceStorageUnavailable,
}
