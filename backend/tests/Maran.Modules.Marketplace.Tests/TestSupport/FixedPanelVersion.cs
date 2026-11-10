using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A panel version fixed for a test.</summary>
public sealed class FixedPanelVersion : IPanelVersion
{
    /// <inheritdoc />
    public string Current { get; } = "1.4.0";
}
