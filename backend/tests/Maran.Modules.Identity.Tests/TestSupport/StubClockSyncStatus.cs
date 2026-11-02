using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IClockSyncStatus"/> answering whatever a test set, so Identity's refusal of a
/// two-factor code can be driven with no Monitoring module in the process.
/// </summary>
public sealed class StubClockSyncStatus : IClockSyncStatus
{
    /// <summary>Whether the latest reading is to be reported as not synchronised; false by default.</summary>
    public bool Unsynchronized { get; set; }

    /// <inheritdoc />
    public Task<bool> IsClockUnsynchronizedAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Unsynchronized);
    }
}
