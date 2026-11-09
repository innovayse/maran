using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>An <see cref="IReservedHostDirectory"/> reserving exactly the names a test registered.</summary>
public sealed class StubReservedHostDirectory : IReservedHostDirectory
{
    /// <summary>The names the panel uses.</summary>
    public HashSet<string> Reserved { get; } = [];

    /// <inheritdoc />
    public bool IsPanelHostName(string candidate)
    {
        return Reserved.Contains(candidate);
    }
}
