using Maran.Modules.Marketplace.Common;

namespace Maran.Modules.Marketplace.Mappers;

/// <summary>Restates the marketplace's switch and configuration as the wire shape.</summary>
public static class MarketplaceStatusMapper
{
    /// <summary>Maps the status.</summary>
    /// <param name="enabled">Whether the marketplace is switched on.</param>
    /// <param name="hostConfigured">Whether the configured marketplace address is usable.</param>
    /// <param name="host">The marketplace host name, or null when no usable address is configured.</param>
    /// <param name="sentItems">The lines saying what the panel sends, in the caller's language.</param>
    /// <param name="restartRequiredModules">The ids of the modules waiting for a restart.</param>
    /// <returns>The wire shape.</returns>
    public static MarketplaceStatusDto ToDto(
        bool enabled,
        bool hostConfigured,
        string? host,
        IReadOnlyList<string> sentItems,
        IReadOnlyList<string> restartRequiredModules)
    {
        return new MarketplaceStatusDto(enabled, hostConfigured, host, sentItems, restartRequiredModules);
    }
}
