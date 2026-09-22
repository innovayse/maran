using Maran.Modules.Licensing.Domain.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// A server identity that answers one value, chosen by the test.
/// </summary>
/// <remarks>
/// It answers <see langword="null"/> by default, and that default is deliberate rather than
/// convenient: null is what a host whose identity cannot be read produces, and a test that forgets
/// to say which machine it is on should see the refusing case, never the permitting one.
/// </remarks>
public sealed class FixedServerIdentitySource : IServerIdentitySource
{
    /// <summary>The machine-id this source reports, or null for "could not be read".</summary>
    private readonly string? _machineId;

    /// <summary>Creates the source.</summary>
    /// <param name="machineId">The machine-id to report, or null for "could not be read".</param>
    public FixedServerIdentitySource(string? machineId = null)
    {
        _machineId = machineId;
    }

    /// <inheritdoc/>
    public Task<string?> TryReadMachineIdAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_machineId);
    }
}
