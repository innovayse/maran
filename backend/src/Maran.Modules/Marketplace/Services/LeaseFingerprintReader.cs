using System.Security.Cryptography;
using System.Text;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Names, without exposing it, the lease credential the panel would send right now. The catalogue's
/// entitlement column is the service's answer to "what may THIS lease do", so a cached page is only the
/// right page while the same lease would still be presented; a new lease (another issue time, another
/// signature) or none at all (it ended, or was never there) is another question and needs another page.
/// </summary>
/// <remarks>
/// <para>
/// The credential is asked from <see cref="ILeaseRequestAuthorizer"/> exactly as a real request asks for
/// it, on a probe message that is never sent, so the answer always agrees with what the transport would
/// attach, including the authorizer's own judgement of when a lease has ended. Only the SHA-256 of the
/// header leaves this type: the token itself is neither kept nor compared, so it cannot end up in a cache
/// key that a memory dump or a log line could show.
/// </para>
/// Registered as scoped, like the authorizer's consumers.
/// </remarks>
public sealed class LeaseFingerprintReader
{
    /// <summary>Attaches the lease credential to a request.</summary>
    private readonly ILeaseRequestAuthorizer _lease;

    /// <summary>Creates the reader.</summary>
    /// <param name="lease">Attaches the lease credential to a request.</param>
    public LeaseFingerprintReader(ILeaseRequestAuthorizer lease)
    {
        _lease = lease;
    }

    /// <summary>Reads the fingerprint of the credential that would be sent now.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lower-case hex SHA-256 of the credential, or empty text when the panel holds no usable lease.</returns>
    public async Task<string> ReadAsync(CancellationToken ct)
    {
        using var probe = new HttpRequestMessage();
        var attached = await _lease.TryAuthorizeAsync(probe, ct);

        if (!attached || probe.Headers.Authorization is not { } credential)
        {
            return string.Empty;
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential.ToString())));
    }
}
