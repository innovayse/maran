namespace Maran.Sdk.Contracts;

/// <summary>
/// What a <see cref="Interfaces.ILicenceLeaseSource"/> found: nothing, a verified lease, or something
/// it refused. Closed to these three cases (the constructor is private), the same shape as the
/// licence's own status, so "no lease" and "a lease that cannot be trusted" are different facts and
/// neither is ever read as the other.
/// </summary>
public abstract record LeaseReading
{
    /// <summary>Prevents any case but the three below.</summary>
    private LeaseReading()
    {
    }

    /// <summary>No lease is stored (the file does not exist, or is empty).</summary>
    public sealed record Absent : LeaseReading
    {
        /// <summary>The one value of the case.</summary>
        public static readonly Absent Instance = new();
    }

    /// <summary>A lease whose signature has been checked.</summary>
    /// <param name="Lease">The verified lease.</param>
    public sealed record Present(LicenceLease Lease) : LeaseReading;

    /// <summary>A lease was stored and could not be trusted.</summary>
    /// <param name="Reason">Why.</param>
    public sealed record Refused(LeaseRefusalReason Reason) : LeaseReading;
}
