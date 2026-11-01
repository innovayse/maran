using System.Diagnostics.CodeAnalysis;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>An <see cref="IPanelHostName"/> double answering with one fixed name, or with none.</summary>
public sealed class StubPanelHostName : IPanelHostName
{
    /// <summary>Whether a name is configured.</summary>
    private readonly bool _configured;

    /// <summary>The configured name; meaningful only when <see cref="_configured"/>.</summary>
    private readonly string _hostName;

    /// <summary>Creates a double with <paramref name="hostName"/> configured.</summary>
    /// <param name="hostName">The configured panel name.</param>
    public StubPanelHostName(string hostName)
    {
        _configured = true;
        _hostName = hostName;
    }

    /// <summary>Creates a double with no usable name configured.</summary>
    public StubPanelHostName()
    {
        _configured = false;
        _hostName = string.Empty;
    }

    /// <inheritdoc />
    public bool TryGet([NotNullWhen(true)] out string? hostName)
    {
        hostName = _configured ? _hostName : null;
        return _configured;
    }
}
