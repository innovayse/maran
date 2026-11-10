using Maran.Modules.Licensing.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>A lease writer whose write always fails, as a full or read-only disk would.</summary>
internal sealed class ThrowingLeaseWriter : ILeaseWriter
{
    /// <inheritdoc />
    public Task WriteAsync(string envelopeText, CancellationToken cancellationToken)
    {
        throw new IOException("Simulated write failure: the atomic rename never completed.");
    }
}
