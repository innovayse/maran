using System.Collections.Concurrent;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Remembers, for the life of this process, which installed modules the last update check found
/// withdrawn, so the installed list can say so without calling the marketplace again.
/// </summary>
/// <remarks>
/// The marketplace is only ever asked when the administrator presses check (no timer, no start-up call), so
/// until the first check this holds nothing and the installed list reports nothing as withdrawn. That is
/// "not yet known", and the screen's check button is how it becomes known.
/// </remarks>
public sealed class ObservedRevocations
{
    /// <summary>The module ids the last check found withdrawn.</summary>
    private readonly ConcurrentDictionary<string, bool> _revoked = new(StringComparer.Ordinal);

    /// <summary>Replaces what is remembered with the result of a check.</summary>
    /// <param name="revokedModuleIds">The module ids the check found withdrawn.</param>
    public void Replace(IEnumerable<string> revokedModuleIds)
    {
        ArgumentNullException.ThrowIfNull(revokedModuleIds);

        _revoked.Clear();
        foreach (var id in revokedModuleIds)
        {
            _revoked[id] = true;
        }
    }

    /// <summary>Tells whether the last check found a module withdrawn.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <returns>True when it did.</returns>
    public bool IsRevoked(string moduleId)
    {
        return _revoked.ContainsKey(moduleId);
    }
}
