using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>An in-memory stand-in for the Host's log level switch, recording every level applied.</summary>
public sealed class FakePanelLogLevelSwitch : IPanelLogLevelSwitch
{
    /// <summary>Every level applied, in order.</summary>
    public List<PanelLogLevel> Applied { get; } = [];

    /// <inheritdoc />
    public PanelLogLevel Current { get; private set; }

    /// <summary>Creates the switch at a starting level.</summary>
    /// <param name="initial">The level it reports before anything is applied.</param>
    public FakePanelLogLevelSwitch(PanelLogLevel initial = PanelLogLevel.Information)
    {
        Current = initial;
    }

    /// <inheritdoc />
    public void Apply(PanelLogLevel level)
    {
        Applied.Add(level);
        Current = level;
    }
}
