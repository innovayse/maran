namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for what an administrator asks of the marketplace client
/// (issues #186, #187): switching it on or off, and requesting an install, update, removal or
/// rollback. Constants rather than an enum, for the reason the other <c>*AuditActions</c> classes are.
/// </summary>
/// <remarks>
/// Distinct from <see cref="MarketplaceAuditActions"/>, which records what the closed loader did to a
/// package: these record what was ASKED, so a refused request leaves an entry even when nothing was
/// loaded. Every constant here needs a display name in the three languages
/// (<c>AuditActionDisplayNameTests</c>). No entry carries a lease, a grant id, a URL or exception text.
/// </remarks>
public static class MarketplaceClientAuditActions
{
    /// <summary>An administrator switched the marketplace on, after re-entering their password.</summary>
    public const string MarketplaceEnabled = "MarketplaceEnabled";

    /// <summary>An administrator switched the marketplace off.</summary>
    public const string MarketplaceDisabled = "MarketplaceDisabled";

    /// <summary>An administrator asked to install a module from the marketplace; the module id is in the subject.</summary>
    public const string ModuleInstallRequested = "ModuleInstallRequested";

    /// <summary>An administrator asked to update an installed module; the module id is in the subject.</summary>
    public const string ModuleUpdateRequested = "ModuleUpdateRequested";

    /// <summary>An administrator asked to remove an installed module; the module id is in the subject.</summary>
    public const string ModuleRemoveRequested = "ModuleRemoveRequested";

    /// <summary>An administrator asked to roll an installed module back; the module id is in the subject.</summary>
    public const string ModuleRollbackRequested = "ModuleRollbackRequested";

    /// <summary>A catalogue, update list or key set failed signature verification and was not trusted.</summary>
    public const string CatalogueVerificationFailed = "CatalogueVerificationFailed";
}
