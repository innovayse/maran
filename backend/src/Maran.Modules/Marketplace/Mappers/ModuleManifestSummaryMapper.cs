using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>Restates the signed manifest summary the service sent as the wire shape the install dialog shows.</summary>
public static class ModuleManifestSummaryMapper
{
    /// <summary>Maps one manifest summary.</summary>
    /// <param name="manifest">The summary the service sent.</param>
    /// <param name="tierDisplayName">The licence tier's name in the caller's language.</param>
    /// <returns>The wire shape.</returns>
    public static ModuleManifestSummaryDto ToDto(CatalogueManifestSummary manifest, string tierDisplayName)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return new ModuleManifestSummaryDto(
            manifest.Publisher,
            manifest.PublisherId,
            tierDisplayName,
            manifest.MinPanelVersion,
            manifest.SchemaName,
            manifest.Dependencies,
            manifest.AgentCapabilities.Select(ItemOf).ToList(),
            manifest.Permissions.Select(ItemOf).ToList());
    }

    /// <summary>Maps one declared item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The wire shape.</returns>
    private static ManifestNamedItemDto ItemOf(CatalogueNamedItem item)
    {
        return new ManifestNamedItemDto(item.Key, item.DisplayName);
    }
}
