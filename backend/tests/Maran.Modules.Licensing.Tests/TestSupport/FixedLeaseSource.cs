using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>A lease source that answers one fixed reading, so a test chooses the lease without touching a file.</summary>
public sealed class FixedLeaseSource : ILicenceLeaseSource
{
    /// <summary>What every read answers.</summary>
    private readonly LeaseReading _reading;

    /// <summary>Creates the source.</summary>
    /// <param name="reading">What every read answers.</param>
    public FixedLeaseSource(LeaseReading reading)
    {
        _reading = reading;
    }

    /// <inheritdoc />
    public Task<LeaseReading> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_reading);
    }
}
