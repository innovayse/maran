using Maran.Sdk.Interfaces;

namespace Maran.Modules.Updates.Tests.TestSupport;

/// <summary>A panel version a test states.</summary>
public sealed class FakePanelVersion : IPanelVersion
{
    /// <inheritdoc />
    public string Current { get; }

    /// <summary>Creates the version window.</summary>
    /// <param name="current">The version the "running panel" has.</param>
    public FakePanelVersion(string current)
    {
        Current = current;
    }
}
