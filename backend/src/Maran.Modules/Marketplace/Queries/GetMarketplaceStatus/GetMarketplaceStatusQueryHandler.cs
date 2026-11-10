using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Queries.GetMarketplaceStatus;

/// <summary>
/// Handles <see cref="GetMarketplaceStatusQuery"/>. It is the only request the page makes while the
/// marketplace is off, so it must not reach the marketplace: it reads the stored switch, the options and the
/// installed modules.
/// </summary>
public sealed class GetMarketplaceStatusQueryHandler
{
    /// <summary>Reads the status.</summary>
    private readonly MarketplaceStatusReader _status;

    /// <summary>Creates the handler.</summary>
    /// <param name="status">Reads the status.</param>
    public GetMarketplaceStatusQueryHandler(MarketplaceStatusReader status)
    {
        _status = status;
    }

    /// <summary>Reads the status.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    public async Task<Result<MarketplaceStatusDto>> HandleAsync(
        GetMarketplaceStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result<MarketplaceStatusDto>.Ok(await _status.ReadAsync(cancellationToken));
    }
}
