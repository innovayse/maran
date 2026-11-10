using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Where the panel reads its current licence lease from. The open default reads
/// <c>/var/lib/maran/lease.json</c> and checks the lease's Ed25519 signature against the lease key
/// pinned in the build; whatever WRITES that file (the daily renewal) is a separate piece and is not
/// part of this seam. An implementation hands over only a lease it has verified and never throws:
/// every failure is a <see cref="LeaseReading.Refused"/> or <see cref="LeaseReading.Absent"/>.
/// </summary>
public interface ILicenceLeaseSource
{
    /// <summary>Reads the stored lease.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reading; never null.</returns>
    Task<LeaseReading> ReadAsync(CancellationToken cancellationToken);
}
