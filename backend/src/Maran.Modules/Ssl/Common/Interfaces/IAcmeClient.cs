
namespace Maran.Modules.Ssl.Common.Interfaces;

/// <summary>
/// Orders a certificate from an ACME certificate authority and returns the issued material.
/// </summary>
/// <remarks>
/// A seam, not an indirection for its own sake: issuance is the one step of this module that talks to
/// a third party over the internet, and every handler test that drives issuance would otherwise need
/// one. The interface promises a <see cref="Result{T}"/> and not an exception — an authority that
/// refuses an order has answered, and the caller acts on that answer (rules/csharp.md "Errors:
/// Result, not exceptions").
/// </remarks>
public interface IAcmeClient
{
    /// <summary>Orders a certificate for one domain, proving control over HTTP-01.</summary>
    /// <param name="request">The domain to order for and the account whose document root answers.</param>
    /// <param name="cancellationToken">Cancellation for the whole order.</param>
    /// <returns>
    /// The issued material, or a typed failure. The failure never carries the authority's own text:
    /// a problem document can quote what it could not parse, and what it could not parse may be key
    /// material (rules/security.md item 8).
    /// </returns>
    Task<Result<IssuedCertificate>> OrderAsync(AcmeOrderRequest request, CancellationToken cancellationToken);
}
