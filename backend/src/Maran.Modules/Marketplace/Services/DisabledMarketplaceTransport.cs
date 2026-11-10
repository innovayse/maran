using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The transport used while the marketplace is switched off. It answers <c>MarketplaceDisabled</c> to
/// everything and has no constructor parameter that could reach the network, so "off" cannot be undone
/// by a code path that forgot to check the switch: this type is the path, and it goes nowhere.
/// </summary>
public sealed class DisabledMarketplaceTransport : IMarketplaceTransport
{
    /// <inheritdoc />
    public Task<Result<MarketplaceResponse>> SendAsync(MarketplaceRequest request, CancellationToken ct)
    {
        return Task.FromResult(Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.Disabled));
    }

    /// <inheritdoc />
    public Task<Result<MarketplaceDownload>> OpenDownloadAsync(Uri url, CancellationToken ct)
    {
        return Task.FromResult(Result<MarketplaceDownload>.Fail(MarketplaceClientRefusals.Disabled));
    }
}
