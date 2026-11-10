namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The service's copy of a package's manifest summary, shown to the administrator before an install. It
/// is the server's statement, not the package's: the loader later compares the package's own signed
/// manifest with what the catalogue announced.
/// </summary>
/// <param name="Publisher">Who published the module.</param>
/// <param name="Tier">The licence tier's wire name.</param>
/// <param name="MinPanelVersion">The oldest panel version the module runs on.</param>
/// <param name="Dependencies">The ids of the modules it depends on.</param>
/// <param name="AgentCapabilities">The parts of the agent the module drives, with localised names.</param>
/// <param name="Permissions">The permissions the module's endpoints use, with localised names.</param>
/// <param name="SchemaName">The database schema the module owns, or null when it owns none.</param>
public sealed record CatalogueManifestSummary(
    string Publisher,
    string Tier,
    string MinPanelVersion,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<CatalogueNamedItem> AgentCapabilities,
    IReadOnlyList<CatalogueNamedItem> Permissions,
    string? SchemaName = null);
