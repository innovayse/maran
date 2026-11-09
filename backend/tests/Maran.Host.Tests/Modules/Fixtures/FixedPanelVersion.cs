using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Modules.Fixtures;

/// <summary>A panel version a test states, so a compatibility check is not read from the ambient assembly.</summary>
internal sealed class FixedPanelVersion : IPanelVersion
{
    /// <summary>Creates the stand-in.</summary>
    /// <param name="current">The version it reports.</param>
    public FixedPanelVersion(string current)
    {
        Current = current;
    }

    /// <inheritdoc />
    public string Current { get; }
}
