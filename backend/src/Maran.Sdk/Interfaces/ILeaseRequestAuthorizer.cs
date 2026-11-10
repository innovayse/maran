namespace Maran.Sdk.Interfaces;

/// <summary>
/// Attaches the panel's licence-lease credential to an outgoing request to the licence-bound services,
/// without handing the credential to the caller. The seam holds a credential, so the module asking for
/// the attachment never sees the token: it passes the request in and learns only whether a credential
/// was attached (rules/architecture.md "A shared facility's contract").
/// </summary>
/// <remarks>
/// The Licensing module answers it once a lease exists. Until then the Sdk registers
/// <see cref="Modules.NoLeaseRequestAuthorizer"/>, which attaches nothing, so every request goes out
/// anonymously and the catalogue is browse-only.
/// </remarks>
public interface ILeaseRequestAuthorizer
{
    /// <summary>Adds the lease credential to <paramref name="request"/> when the panel holds a valid lease.</summary>
    /// <param name="request">The request about to be sent; its authorization header is set when a credential exists.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when a credential was attached; false when the panel holds no usable lease.</returns>
    Task<bool> TryAuthorizeAsync(HttpRequestMessage request, CancellationToken ct);
}
