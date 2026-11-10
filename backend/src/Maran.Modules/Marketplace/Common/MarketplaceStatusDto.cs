namespace Maran.Modules.Marketplace.Common;

/// <summary>What <c>GET /api/v1/marketplace/status</c> answers: the switch, and what switching it on sends.</summary>
/// <param name="Enabled">Whether an administrator has switched the marketplace on. Off until someone does.</param>
/// <param name="HostConfigured">Whether the configured marketplace address is usable.</param>
/// <param name="Host">The marketplace's host name only, or null when no usable address is configured.</param>
/// <param name="SentItems">The localized lines saying what the panel sends the marketplace.</param>
/// <param name="RestartRequiredModules">Ids of the modules staged and waiting for a panel restart.</param>
public sealed record MarketplaceStatusDto(
    bool Enabled,
    bool HostConfigured,
    string? Host,
    IReadOnlyList<string> SentItems,
    IReadOnlyList<string> RestartRequiredModules);
