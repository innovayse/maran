using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Marketplace.Domain.Entities;

/// <summary>
/// The highest version of one module this panel has ever staged. It only ever goes up, and it is what the
/// catalogue install path compares a candidate against, so a replayed older catalogue cannot walk a module
/// back to a version with a known flaw.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own row, not a column of <see cref="InstalledPackage"/>.</b> The floor outlives the history it
/// was learned from: removing a module closes its package rows, and the floor must still refuse an older
/// reinstall. One fact, one row, one meaning.
/// </para>
/// <para>
/// A deliberate, explicit rollback to the previous version is the loader's own path and does not lower
/// this value; the ledger records it, and the floor stays where it was.
/// </para>
/// </remarks>
public sealed class ModuleVersionFloor
{
    /// <summary>The module's machine id; the row's key.</summary>
    public string ModuleId { get; private set; }

    /// <summary>The highest semantic version staged for the module so far.</summary>
    public string HighestVersionSeen { get; private set; }

    /// <summary>When the value last rose.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Starts a module's floor at the first version staged for it.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="version">The first staged version.</param>
    /// <param name="at">The instant.</param>
    public ModuleVersionFloor(string moduleId, string version, DateTimeOffset at)
    {
        ModuleId = moduleId;
        HighestVersionSeen = version;
        UpdatedAt = at;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ModuleVersionFloor()
    {
        ModuleId = string.Empty;
        HighestVersionSeen = string.Empty;
    }

    /// <summary>Raises the floor to <paramref name="version"/> when that is higher; a lower or equal version changes nothing.</summary>
    /// <param name="version">The version just staged.</param>
    /// <param name="at">The instant.</param>
    /// <returns>True when the floor rose.</returns>
    /// <remarks>
    /// A version that does not parse never raises it, so the floor can only be moved by a version the panel
    /// can compare.
    /// </remarks>
    public bool Raise(string version, DateTimeOffset at)
    {
        if (!SemanticVersion.TryParse(version, out var candidate) || candidate is null)
        {
            return false;
        }

        if (SemanticVersion.TryParse(HighestVersionSeen, out var current)
            && current is not null
            && SemanticVersion.Compare(candidate, current) <= 0)
        {
            return false;
        }

        HighestVersionSeen = version;
        UpdatedAt = at;
        return true;
    }
}
