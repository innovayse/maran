using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Domain.Entities;

/// <summary>
/// One entry in the history of a module package this panel staged from the marketplace: which version,
/// which package digest, who asked, when, and what became of it. Together the rows are the answer to
/// "what happened to this module here", used for rollback and for the downgrade check.
/// </summary>
/// <remarks>
/// <para>
/// <b>History, never the truth of what runs.</b> What is loaded right now is the loader's answer
/// (<c>IModuleLoader.ListAsync</c>); this row records that the panel STAGED a package, which is why its
/// first state is <see cref="InstalledPackageState.Staged"/> and not "installed". A screen assembled from
/// these rows alone would agree with the panel by construction and report a module the loader had
/// refused.
/// </para>
/// <para>
/// <b>Not tenant-scoped.</b> The marketplace is panel-wide and administrator-only, so the row carries no
/// <c>AccountId</c> and no query filter applies; the policy on the controller is the authorisation.
/// </para>
/// <para>
/// <b>No lease, grant, URL or exception text is stored</b> (rules/security.md item 8): only the module id,
/// the version, the package digest and the administrator's id.
/// </para>
/// </remarks>
public sealed class InstalledPackage
{
    /// <summary>The longest module name kept with a row, in characters.</summary>
    public const int DisplayNameMaxLength = 128;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The module's stable machine id, equal to its manifest id.</summary>
    public string ModuleId { get; private set; }

    /// <summary>The semantic version of the staged package.</summary>
    public string Version { get; private set; }

    /// <summary>The SHA-256 of the package file, as lower-case hexadecimal.</summary>
    public string PackageSha256 { get; private set; }

    /// <summary>When the package was staged.</summary>
    public DateTimeOffset InstalledAt { get; private set; }

    /// <summary>The administrator who asked for it.</summary>
    public Guid InstalledBy { get; private set; }

    /// <summary>The version this package replaced, or null for the module's first package.</summary>
    public string? PreviousVersion { get; private set; }

    /// <summary>What became of this package.</summary>
    public InstalledPackageState State { get; private set; }

    /// <summary>When <see cref="State"/> last changed; equals <see cref="InstalledAt"/> until it does.</summary>
    public DateTimeOffset StateChangedAt { get; private set; }

    /// <summary>
    /// The catalogue's name for the module at the time this package was staged, in the language the
    /// administrator's request asked for; null for a row written before the name was kept, which the
    /// installed list shows by the module id.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>Records that a verified package was staged; it activates when the panel next starts.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="version">The staged package's semantic version.</param>
    /// <param name="packageSha256">The package file's SHA-256, lower-case hexadecimal.</param>
    /// <param name="installedBy">The administrator who asked for it.</param>
    /// <param name="previousVersion">The version it replaces, or null when the module had none.</param>
    /// <param name="at">The instant, taken from <see cref="IClock"/>.</param>
    /// <param name="displayName">The catalogue's name for the module, or null when none is known.</param>
    public InstalledPackage(
        Guid id,
        string moduleId,
        string version,
        string packageSha256,
        Guid installedBy,
        string? previousVersion,
        DateTimeOffset at,
        string? displayName = null)
    {
        Id = id;
        ModuleId = moduleId;
        Version = version;
        PackageSha256 = packageSha256;
        InstalledBy = installedBy;
        PreviousVersion = previousVersion;
        InstalledAt = at;
        State = InstalledPackageState.Staged;
        StateChangedAt = at;
        DisplayName = displayName;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private InstalledPackage()
    {
        ModuleId = string.Empty;
        Version = string.Empty;
        PackageSha256 = string.Empty;
    }

    /// <summary>Records that the panel started after staging and composed this package's module.</summary>
    /// <param name="at">The instant of the change.</param>
    /// <remarks>Repeating it on an active row changes nothing, so a status read that reconciles twice is harmless.</remarks>
    /// <exception cref="InvalidOperationException">The row was superseded or removed, which a caller cannot reach by reading the current row.</exception>
    public void MarkActive(DateTimeOffset at)
    {
        if (State == InstalledPackageState.Active)
        {
            return;
        }

        RequireOpen();
        State = InstalledPackageState.Active;
        StateChangedAt = at;
    }

    /// <summary>Records that a later package replaced this one: an upgrade, or a rollback to an older one.</summary>
    /// <param name="at">The instant of the change.</param>
    /// <exception cref="InvalidOperationException">The row was already superseded or removed.</exception>
    public void Supersede(DateTimeOffset at)
    {
        RequireOpen();
        State = InstalledPackageState.Superseded;
        StateChangedAt = at;
    }

    /// <summary>Records that the module was removed. Its data is kept; only the package is gone.</summary>
    /// <param name="at">The instant of the change.</param>
    /// <exception cref="InvalidOperationException">The row was already superseded or removed.</exception>
    public void Remove(DateTimeOffset at)
    {
        RequireOpen();
        State = InstalledPackageState.Removed;
        StateChangedAt = at;
    }

    /// <summary>Refuses a transition out of a state the history has already closed.</summary>
    /// <exception cref="InvalidOperationException">The row is superseded or removed.</exception>
    private void RequireOpen()
    {
        if (State is InstalledPackageState.Superseded or InstalledPackageState.Removed)
        {
            throw new InvalidOperationException(
                $"A {State} package row is closed history and cannot change state again.");
        }
    }
}
