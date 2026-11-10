using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>A lease source that counts its reads and can be made to throw, to observe the cache and the window's failure path.</summary>
public sealed class CountingLeaseSource : ILicenceLeaseSource
{
    /// <summary>What a read answers when it does not throw.</summary>
    private readonly LeaseReading _reading;

    /// <summary>Creates the source.</summary>
    /// <param name="reading">What a read answers.</param>
    /// <param name="throws">Whether a read throws instead.</param>
    public CountingLeaseSource(LeaseReading reading, bool throws = false)
    {
        _reading = reading;
        Throws = throws;
    }

    /// <summary>How many reads have been made.</summary>
    public int Reads { get; private set; }

    /// <summary>Whether a read throws.</summary>
    public bool Throws { get; set; }

    /// <inheritdoc />
    public Task<LeaseReading> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Throws ? throw new InvalidOperationException("planted lease source failure") : Task.FromResult(_reading);
    }
}
