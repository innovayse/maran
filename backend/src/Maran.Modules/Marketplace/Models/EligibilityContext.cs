namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The panel-side facts one eligibility decision needs, read once so a page of modules is judged against
/// one consistent picture.
/// </summary>
/// <param name="MarketplaceEnabled">Whether an administrator has switched the marketplace on.</param>
/// <param name="Installed">The installed modules, by module id.</param>
/// <param name="ModulesInOperation">The ids of the modules an install, update, remove or rollback is running on.</param>
public sealed record EligibilityContext(
    bool MarketplaceEnabled,
    IReadOnlyDictionary<string, InstalledModuleFacts> Installed,
    IReadOnlySet<string> ModulesInOperation);
