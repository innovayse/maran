using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Models;

/// <summary>
/// The outcome of one attempt at a signed ACME request, before any retry decision: exactly one of
/// three cases, each its own type, so "refused" and "refused for a stale nonce" are not a pair of
/// flags whose impossible combination is still representable.
/// </summary>
/// <remarks>
/// It exists so the retry decision and the result conversion are two separable steps. Folding them
/// together produced a method that could either retry or return, and no way to test the retry
/// condition without a live authority. The constructor is private to this assembly, so
/// <see cref="AcceptedAcmeAttempt"/>, <see cref="StaleNonceAcmeAttempt"/> and
/// <see cref="RefusedAcmeAttempt"/> are the whole set and a switch over them is exhaustive by
/// construction.
/// </remarks>
public abstract record AcmeAttempt
{
    /// <summary>The response body as text. Never returned to a caller on the failure path.</summary>
    public string Body { get; }

    /// <summary>Closes the hierarchy to the three attempts this module declares.</summary>
    /// <param name="body">The response body as text.</param>
    private protected AcmeAttempt(string body)
    {
        Body = body;
    }

    /// <summary>Converts the attempt into the result its caller returns.</summary>
    /// <returns>The body on success; on failure a code, never the authority's own text.</returns>
    /// <remarks>
    /// The failure branch deliberately discards <see cref="Body"/>. A problem document is written for
    /// an operator and can quote whatever the authority could not parse, so it is logged where it is
    /// produced and never carried outward (rules/security.md item 8).
    /// </remarks>
    public Result<string> ToResult()
    {
        return this is AcceptedAcmeAttempt
            ? Result<string>.Ok(Body)
            : Result<string>.Fail(Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
    }
}
