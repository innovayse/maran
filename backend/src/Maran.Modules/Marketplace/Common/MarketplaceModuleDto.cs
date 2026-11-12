using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Common;

/// <summary>One module of the catalogue as the Marketplace screen shows it.</summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The module's name.</param>
/// <param name="Summary">A one-line description, plain text.</param>
/// <param name="Publisher">The publisher's display name; what the screen prints.</param>
/// <param name="PublisherId">The publisher's stable id, or null when the service sent none; never printed.</param>
/// <param name="Tier">The licence tier as the service names it.</param>
/// <param name="TierDisplayName">The tier in the caller's language.</param>
/// <param name="Price">The price, or null when the service states none.</param>
/// <param name="LatestVersion">The newest version offered, or null when none is.</param>
/// <param name="InstalledVersion">The version installed on this panel, or null when the module is not installed.</param>
/// <param name="State">Where the module stands on this panel.</param>
/// <param name="Entitlement">Whether the licence entitles the panel to the module.</param>
/// <param name="CanInstall">Whether the newest version can be installed now; decided by the server, never by the screen.</param>
/// <param name="CannotInstallMessage">Why not, in the caller's language; null when <paramref name="CanInstall"/> is true.</param>
/// <param name="RestartRequired">Whether the module is staged and waits for a panel restart.</param>
public sealed record MarketplaceModuleDto(
    string Id,
    string DisplayName,
    string Summary,
    string Publisher,
    string? PublisherId,
    string Tier,
    string TierDisplayName,
    MarketplacePriceDto? Price,
    string? LatestVersion,
    string? InstalledVersion,
    MarketplaceModuleState State,
    MarketplaceEntitlementDto Entitlement,
    bool CanInstall,
    string? CannotInstallMessage,
    bool RestartRequired);
