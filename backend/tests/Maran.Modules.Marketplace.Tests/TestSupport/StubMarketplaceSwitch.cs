using Maran.Modules.Marketplace.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A switch a test sets to on or off.</summary>
public sealed class StubMarketplaceSwitch : IMarketplaceSwitch
{
    /// <summary>The answer returned.</summary>
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        return Task.FromResult(Enabled);
    }
}
