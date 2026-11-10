using System.Buffers.Text;
using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The one spelling of the licence lease as a bearer credential on a request to the licence-bound services
/// (docs/marketplace/api-v1.md "Authentication"): the signed lease exactly as the licence service issued
/// it, <c>base64url(payload) "." base64url(signature)</c>, both without padding.
/// </summary>
/// <remarks>
/// <para>
/// <b>The credential is the service's own signed statement, not something the panel makes.</b> The panel
/// holds no private key of any kind, so it cannot sign a request; what it can do is hand back the lease
/// the service signed. The service verifies its own Ed25519 signature over the decoded payload, checks
/// <c>kind</c>, <c>validUntil</c> and the licence, and binds a grant to the licence and to the lease's
/// <c>server</c> member. The panel adds nothing to it and removes nothing from it, so the bytes the
/// service verifies are the bytes it issued.
/// </para>
/// <para>
/// <b>What it contains</b> is what the lease contains: the licence id, the issue and end instants and the
/// machine id the lease is bound to, if any. No host name, account, site, user or customer data exists in
/// it to send, and none is added (rules/security.md item 10).
/// </para>
/// </remarks>
public static class LeaseBearerToken
{
    /// <summary>The character between the two encoded parts; it is not in the base64url alphabet.</summary>
    public const char Separator = '.';

    /// <summary>Writes the bearer credential for a verified lease.</summary>
    /// <param name="lease">The lease's signed payload.</param>
    /// <returns>The credential, safe to place in an <c>Authorization: Bearer</c> header.</returns>
    public static string Of(SignedLeasePayload lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return Base64Url.EncodeToString(lease.PayloadBytes) + Separator + Base64Url.EncodeToString(lease.Signature);
    }
}
