using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Where the module reads the signed bytes of the stored lease from, for presenting it to the licence
/// service. Separate from the Sdk's <c>ILicenceLeaseSource</c> on purpose: that seam hands the panel a
/// verified lease with the signature removed, which is all a coverage decision needs, while this one hands
/// over the signed bytes and so is for the module's own outbound credential and nothing else.
/// </summary>
public interface ILeaseEnvelopeSource
{
    /// <summary>Reads the stored lease's signed payload.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The payload bytes and signature of a lease whose signature verified against the pinned lease key;
    /// null when there is no lease or it cannot be trusted. Never throws.
    /// </returns>
    Task<SignedLeasePayload?> ReadSignedAsync(CancellationToken cancellationToken);
}
