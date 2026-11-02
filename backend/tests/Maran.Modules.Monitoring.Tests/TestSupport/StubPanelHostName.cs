using System.Diagnostics.CodeAnalysis;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>An <see cref="IPanelHostName"/> double answering with one fixed name, or with none.</summary>
public sealed class StubPanelHostName : IPanelHostName
{
    /// <summary>The configured name, or <see langword="null"/> when none is.</summary>
    private readonly string? _hostName;

    /// <summary>Creates a double with <paramref name="hostName"/> configured, or none when it is null.</summary>
    /// <param name="hostName">The configured panel name, or null for none.</param>
    public StubPanelHostName(string? hostName)
    {
        _hostName = hostName;
    }

    /// <inheritdoc />
    public bool TryGet([NotNullWhen(true)] out string? hostName)
    {
        hostName = _hostName;
        return _hostName is not null;
    }
}
