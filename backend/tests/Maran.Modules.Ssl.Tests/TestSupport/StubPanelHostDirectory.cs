using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IReservedHostDirectory"/> double that treats a fixed set of names, and their
/// <c>www.</c> forms like the real implementation, as the panel's own, compared case-insensitively.
/// Built with no names it answers false for everything, which is how the real implementation
/// answers on a host with no panel name configured.
/// </summary>
public sealed class StubPanelHostDirectory : IReservedHostDirectory
{
    /// <summary>The names this double calls the panel's own.</summary>
    private readonly HashSet<string> _panelNames;

    /// <summary>Creates a double that reserves the given names.</summary>
    /// <param name="panelNames">The names to answer true for; none means every answer is false.</param>
    public StubPanelHostDirectory(params string[] panelNames)
    {
        _panelNames = new HashSet<string>(panelNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsPanelHostName(string candidate)
    {
        return _panelNames.Contains(candidate)
            || (candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                && _panelNames.Contains(candidate[4..]));
    }
}
