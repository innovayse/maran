using Maran.Modules.Licensing.Models;

namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// The panel's one question to the licence service: a lease for this licence. Separate from the HTTP client
/// behind it so the renewal's judgement is tested without a network, and so the public demonstration can
/// substitute a client that makes no call.
/// </summary>
public interface ILeaseServiceClient
{
    /// <summary>Asks the service for a lease.</summary>
    /// <param name="licenceId">The installed licence's id. The only thing the request carries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer, classified. Never throws for a network failure.</returns>
    Task<LeaseServiceAnswer> RequestAsync(string licenceId, CancellationToken cancellationToken);
}
