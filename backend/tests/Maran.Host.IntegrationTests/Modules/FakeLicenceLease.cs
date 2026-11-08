using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// A licence lease a test controls: which modules it covers, and whether it has lapsed. It holds no
/// decryption key at all - a fake that held one would be a second place a key could leak from - so
/// <see cref="DecryptionKeyFor"/> always answers that there is none.
/// </summary>
public sealed class FakeLicenceLease : ILicenceLease
{
    /// <summary>The modules the lease covers.</summary>
    private readonly HashSet<string> _entitled = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public DateTimeOffset ExpiresAt { get; private set; } = DateTimeOffset.MaxValue;

    /// <summary>Whether the lease has lapsed, which uncovers every module at once.</summary>
    public bool Lapsed { get; private set; }

    /// <summary>Covers a module.</summary>
    /// <param name="moduleId">The module's id.</param>
    public void Entitle(string moduleId)
    {
        _entitled.Add(moduleId);
    }

    /// <summary>Lapses the lease: from now on it covers nothing.</summary>
    public void Lapse()
    {
        Lapsed = true;
        ExpiresAt = DateTimeOffset.MinValue;
    }

    /// <inheritdoc />
    public bool IsEntitled(string moduleId)
    {
        return !Lapsed && _entitled.Contains(moduleId);
    }

    /// <inheritdoc />
    public byte[]? DecryptionKeyFor(string moduleId)
    {
        return null;
    }
}
