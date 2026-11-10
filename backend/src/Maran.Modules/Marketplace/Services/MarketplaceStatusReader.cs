using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Mappers;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Reads the marketplace's status: the switch, whether the configured address is usable, what switching it
/// on sends, and the modules waiting for a restart. Makes no marketplace call, so the page can render its
/// switch card with the marketplace off. Shared by the status query and the switch command, which both
/// answer with it.
/// </summary>
public sealed class MarketplaceStatusReader
{
    /// <summary>The stored switch.</summary>
    private readonly MarketplaceSettingsStore _settings;

    /// <summary>The validated options.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The installed modules.</summary>
    private readonly InstalledModuleReader _installed;

    /// <summary>The words, in the caller's language.</summary>
    private readonly MarketplaceDisplayNames _names;

    /// <summary>Creates the reader.</summary>
    /// <param name="settings">The stored switch.</param>
    /// <param name="options">The validated options.</param>
    /// <param name="installed">The installed modules.</param>
    /// <param name="names">The words, in the caller's language.</param>
    public MarketplaceStatusReader(
        MarketplaceSettingsStore settings,
        MarketplaceOptionsReader options,
        InstalledModuleReader installed,
        MarketplaceDisplayNames names)
    {
        _settings = settings;
        _options = options;
        _installed = installed;
        _names = names;
    }

    /// <summary>Reads the status.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status as the screen shows it.</returns>
    public async Task<MarketplaceStatusDto> ReadAsync(CancellationToken ct)
    {
        var enabled = await _settings.IsEnabledAsync(ct);
        var options = _options.Read();
        var hostConfigured = options.IsSuccess;
        var host = hostConfigured ? MarketplaceHostPolicy.BaseUri(options.Value).IdnHost : null;

        var waiting = (await _installed.ReadAsync(ct))
            .Where(facts => { return InstalledModuleReader.ActionsOf(facts).RestartRequired; })
            .Select(facts => { return facts.Loaded.ModuleId; })
            .ToList();

        return MarketplaceStatusMapper.ToDto(enabled, hostConfigured, host, _names.SentItems(), waiting);
    }
}
