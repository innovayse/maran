using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Models;

/// <summary>
/// One judged lease envelope: what its text amounted to, and the signed bytes when it amounted to a lease whose
/// signature held. The reading and the bytes come from one parse, so they cannot describe two leases.
/// </summary>
/// <param name="Reading">Absent, a verified lease, or the reason the text was refused.</param>
/// <param name="Verified">The payload bytes and signature that verified; null unless <paramref name="Reading"/> is a verified lease.</param>
public sealed record LeaseEnvelopeReading(LeaseReading Reading, SignedLeasePayload? Verified)
{
    /// <summary>A text that held no verified lease.</summary>
    /// <param name="reading">Absent or refused.</param>
    /// <returns>The judgement, with no signed bytes.</returns>
    public static LeaseEnvelopeReading Of(LeaseReading reading)
    {
        return new LeaseEnvelopeReading(reading, null);
    }
}
