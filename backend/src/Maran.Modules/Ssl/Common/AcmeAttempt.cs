using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Common;

/// <summary>The outcome of one attempt at a signed ACME request, before any retry decision.</summary>
/// <remarks>
/// It exists so the retry decision and the result conversion are two separable steps. Folding them
/// together produced a method that could either retry or return, and no way to test the retry
/// condition without a live authority.
/// </remarks>
/// <param name="Succeeded">Whether the authority answered with a success status.</param>
/// <param name="RetryableNonce">Whether the refusal was specifically a stale nonce, which is worth one retry.</param>
/// <param name="Body">The response body as text. Never returned to a caller on the failure path.</param>
public sealed record AcmeAttempt(bool Succeeded, bool RetryableNonce, string Body)
{
    /// <summary>Converts the attempt into the result its caller returns.</summary>
    /// <returns>The body on success; on failure a code, never the authority's own text.</returns>
    /// <remarks>
    /// The failure branch deliberately discards <see cref="Body"/>. A problem document is written for
    /// an operator and can quote whatever the authority could not parse, so it is logged where it is
    /// produced and never carried outward (rules/security.md item 8).
    /// </remarks>
    public Result<string> ToResult()
    {
        return Succeeded
            ? Result<string>.Ok(Body)
            : Result<string>.Fail(Error.Of(nameof(ErrorMessages.AcmeOrderRejected)));
    }
}
