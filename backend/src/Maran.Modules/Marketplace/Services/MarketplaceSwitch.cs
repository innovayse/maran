using Maran.Modules.Marketplace.Interfaces;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Answers the client's one question about the switch from the stored decision. It is a window onto
/// <see cref="MarketplaceSettingsStore"/> and nothing more, so the client depends on the question and not
/// on the table that holds the answer.
/// </summary>
public sealed class MarketplaceSwitch : IMarketplaceSwitch
{
    /// <summary>The store that keeps the switch.</summary>
    private readonly MarketplaceSettingsStore _store;

    /// <summary>Creates the window.</summary>
    /// <param name="store">The store that keeps the switch.</param>
    public MarketplaceSwitch(MarketplaceSettingsStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        return _store.IsEnabledAsync(ct);
    }
}
