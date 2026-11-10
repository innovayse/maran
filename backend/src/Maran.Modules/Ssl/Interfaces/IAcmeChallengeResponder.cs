using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Services;

namespace Maran.Modules.Ssl.Interfaces;

/// <summary>
/// Answers the challenges of an order's authorizations with one challenge type, and waits, bounded, for the
/// authority to accept them. One implementation per type: HTTP-01 and DNS-01.
/// </summary>
internal interface IAcmeChallengeResponder
{
    /// <summary>Proves control of every name the order's authorizations cover.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="authorizationUrls">Every authorization of the order; an already-valid one is skipped.</param>
    /// <param name="request">The domain and where its proof is placed.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>Success once every authorization is valid, or a typed failure carrying only a code.</returns>
    /// <remarks>
    /// Whatever the responder placed it removes again on every path — success, refusal, a failed call and
    /// cancellation — and a removal that fails never turns a validation that succeeded into a failure.
    /// </remarks>
    Task<Result> RespondAsync(
        AcmeSession session,
        IReadOnlyList<string> authorizationUrls,
        AcmeOrderRequest request,
        CancellationToken cancellationToken);
}
