namespace Maran.Modules.Marketplace.Common;

/// <summary>
/// The server's copy of a module's signed manifest summary, which the install dialog shows BEFORE the
/// administrator confirms. It is never text from the package itself.
/// </summary>
/// <param name="Publisher">Who publishes the module.</param>
/// <param name="TierDisplayName">The licence tier in the caller's language.</param>
/// <param name="MinPanelVersion">The oldest panel version the module runs on.</param>
/// <param name="SchemaName">The database schema the module owns, or null when it owns none.</param>
/// <param name="Dependencies">The modules it depends on.</param>
/// <param name="AgentCapabilities">The parts of the root agent it declares it drives.</param>
/// <param name="Permissions">The permissions it declares.</param>
public sealed record ModuleManifestSummaryDto(
    string Publisher,
    string TierDisplayName,
    string MinPanelVersion,
    string? SchemaName,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<ManifestNamedItemDto> AgentCapabilities,
    IReadOnlyList<ManifestNamedItemDto> Permissions);
