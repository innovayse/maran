namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// The error codes the marketplace module raises, spelled once for the tests that assert them. The module's
/// own code says <c>nameof(ErrorMessages.X)</c>, but the class MSBuild generates from the neutral resx is
/// <c>internal</c>, as in every shipped module, so a test project cannot name it. The hazard of a spelled
/// string is drift from the resx; <c>OperationCodesResourceTests</c> closes it by checking every constant
/// here against all three resx files.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The code <c>MarketplaceDisabled</c>.</summary>
    public const string MarketplaceDisabled = "MarketplaceDisabled";

    /// <summary>The code <c>MarketplaceHostNotConfigured</c>.</summary>
    public const string MarketplaceHostNotConfigured = "MarketplaceHostNotConfigured";

    /// <summary>The code <c>MarketplaceUnreachable</c>.</summary>
    public const string MarketplaceUnreachable = "MarketplaceUnreachable";

    /// <summary>The code <c>CatalogueUnsigned</c>.</summary>
    public const string CatalogueUnsigned = "CatalogueUnsigned";

    /// <summary>The code <c>CatalogueStale</c>.</summary>
    public const string CatalogueStale = "CatalogueStale";

    /// <summary>The code <c>CatalogueKeyMissing</c>.</summary>
    public const string CatalogueKeyMissing = "CatalogueKeyMissing";

    /// <summary>The code <c>LeaseRequired</c>.</summary>
    public const string LeaseRequired = "LeaseRequired";

    /// <summary>The code <c>EntitlementMissing</c>.</summary>
    public const string EntitlementMissing = "EntitlementMissing";

    /// <summary>The code <c>IncompatiblePanel</c>.</summary>
    public const string IncompatiblePanel = "IncompatiblePanel";

    /// <summary>The code <c>PackageRevoked</c>.</summary>
    public const string PackageRevoked = "PackageRevoked";

    /// <summary>The code <c>GrantHostRefused</c>.</summary>
    public const string GrantHostRefused = "GrantHostRefused";

    /// <summary>The code <c>DownloadSizeMismatch</c>.</summary>
    public const string DownloadSizeMismatch = "DownloadSizeMismatch";

    /// <summary>The code <c>DownloadDigestMismatch</c>.</summary>
    public const string DownloadDigestMismatch = "DownloadDigestMismatch";

    /// <summary>The code <c>ModuleNotInCatalogue</c>.</summary>
    public const string ModuleNotInCatalogue = "ModuleNotInCatalogue";

    /// <summary>The code <c>ModuleNotInstalled</c>.</summary>
    public const string ModuleNotInstalled = "ModuleNotInstalled";

    /// <summary>The code <c>ModuleOperationInProgress</c>.</summary>
    public const string ModuleOperationInProgress = "ModuleOperationInProgress";

    /// <summary>The code <c>ModuleVersionNotInstallable</c>.</summary>
    public const string ModuleVersionNotInstallable = "ModuleVersionNotInstallable";

    /// <summary>The code <c>InstallNotAcknowledged</c>.</summary>
    public const string InstallNotAcknowledged = "InstallNotAcknowledged";

    /// <summary>The code <c>MarketplaceResponseInvalid</c>.</summary>
    public const string MarketplaceResponseInvalid = "MarketplaceResponseInvalid";

    /// <summary>The code <c>MarketplaceResponseTooLarge</c>.</summary>
    public const string MarketplaceResponseTooLarge = "MarketplaceResponseTooLarge";

    /// <summary>The code <c>MarketplaceRedirectRefused</c>.</summary>
    public const string MarketplaceRedirectRefused = "MarketplaceRedirectRefused";

    /// <summary>The code <c>MarketplaceRequestRefused</c>.</summary>
    public const string MarketplaceRequestRefused = "MarketplaceRequestRefused";

    /// <summary>The code <c>MarketplaceStorageUnavailable</c>.</summary>
    public const string MarketplaceStorageUnavailable = "MarketplaceStorageUnavailable";

    /// <summary>The code <c>ModuleAlreadyInstalled</c>.</summary>
    public const string ModuleAlreadyInstalled = "ModuleAlreadyInstalled";

    /// <summary>The code <c>ModuleHasNoPreviousVersion</c>.</summary>
    public const string ModuleHasNoPreviousVersion = "ModuleHasNoPreviousVersion";

    /// <summary>The code <c>ModuleOperationFailed</c>.</summary>
    public const string ModuleOperationFailed = "ModuleOperationFailed";

    /// <summary>The code <c>ModuleIdInvalid</c>.</summary>
    public const string ModuleIdInvalid = "ModuleIdInvalid";

    /// <summary>The code <c>ModuleVersionInvalid</c>.</summary>
    public const string ModuleVersionInvalid = "ModuleVersionInvalid";

    /// <summary>The code <c>MarketplaceChannelInvalid</c>.</summary>
    public const string MarketplaceChannelInvalid = "MarketplaceChannelInvalid";

    /// <summary>The code <c>MarketplaceSearchInvalid</c>.</summary>
    public const string MarketplaceSearchInvalid = "MarketplaceSearchInvalid";

    /// <summary>The code <c>MarketplaceCursorInvalid</c>.</summary>
    public const string MarketplaceCursorInvalid = "MarketplaceCursorInvalid";
}
