using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Interfaces;

/// <summary>
/// The one door to the marketplace host. Everything that crosses it is bytes and headers: the transport
/// knows nothing of catalogues or signatures, and everything above it knows nothing of sockets. The real
/// implementation talks HTTPS to the single configured host; the disabled one answers without touching
/// the network, which is what makes "switched off" a property of the type that is registered and not of
/// every caller remembering to check.
/// </summary>
public interface IMarketplaceTransport
{
    /// <summary>Sends one request to the configured host and returns the answer, whatever its status.</summary>
    /// <param name="request">What to send; the path is relative to the configured host.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The answer with its status, body (capped) and headers; or a coded failure when no answer could be
    /// used: nothing reached, the answer over its cap, a redirect off the host, no host configured, no
    /// lease for a request that needs one, or the marketplace switched off.
    /// </returns>
    Task<Result<MarketplaceResponse>> SendAsync(MarketplaceRequest request, CancellationToken ct);

    /// <summary>Opens a package download as a stream, following only redirects that stay on the configured host.</summary>
    /// <param name="url">The grant's download address; it must be on the configured host.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The open stream, which the caller disposes; or a coded failure.</returns>
    Task<Result<MarketplaceDownload>> OpenDownloadAsync(Uri url, CancellationToken ct);
}
