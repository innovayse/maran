using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Persistence;
using Maran.SharedKernel.Utilities.Text;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The panel's record of the packages it staged from the marketplace: the history of each module and the
/// highest version ever seen for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>History and a floor, never the truth of what runs.</b> What is loaded is the loader's answer
/// (<c>IModuleLoader.ListAsync</c>); a screen reads the state from there and the history from here. The
/// ledger exists so rollback has a previous version to name and so the install path has a floor to refuse
/// a replayed older catalogue against.
/// </para>
/// <para>
/// <b>The floor is monotonic.</b> <see cref="RecordStagedAsync"/> raises it and nothing lowers it: a
/// rollback is recorded as a staged older package and leaves the floor where it was, and removing a module
/// closes its history without touching its floor, so an older reinstall is still refused.
/// </para>
/// <para>
/// Each method saves its own change in one transaction, and holds no lease, grant, URL or error text: the
/// only inputs it accepts are the module id, version, digest, administrator and the catalogue's display name for the module (a name the signed catalogue gave, shortened to its column).
/// </para>
/// </remarks>
public sealed class InstalledPackageLedger
{
    /// <summary>The module's own database context.</summary>
    private readonly MarketplaceDbContext _context;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the ledger.</summary>
    /// <param name="context">The module's database context.</param>
    /// <param name="clock">The time source.</param>
    public InstalledPackageLedger(MarketplaceDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <summary>
    /// Records that a verified package was staged: an install, an update, or a rollback to an older package,
    /// which differ only in the version and not in what the ledger writes.
    /// </summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="version">The staged package's semantic version.</param>
    /// <param name="packageSha256">The package file's SHA-256, lower-case hexadecimal.</param>
    /// <param name="installedBy">The administrator who asked for it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="displayName">The catalogue's name for the module, kept with the row so the installed list can show it offline; null when none is known, and shortened to what the column holds.</param>
    /// <returns>The new row. The module's earlier open row, if any, is superseded and named as its previous version.</returns>
    /// <remarks>
    /// The new row, the superseded one and the floor change in one save, so the history never shows two open
    /// packages for a module and the floor never trails the newest row.
    /// </remarks>
    public async Task<InstalledPackage> RecordStagedAsync(
        string moduleId,
        string version,
        string packageSha256,
        Guid installedBy,
        CancellationToken cancellationToken,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageSha256);

        var now = _clock.UtcNow;
        var current = await FindOpenRowsOf(moduleId)
            .OrderByDescending(package => package.InstalledAt)
            .FirstOrDefaultAsync(cancellationToken);
        current?.Supersede(now);

        var staged = new InstalledPackage(
            Guid.NewGuid(),
            moduleId,
            version,
            packageSha256,
            installedBy,
            current?.Version,
            now,
            NameToKeep(displayName));
        _context.InstalledPackages.Add(staged);

        var floor = await _context.ModuleVersionFloors.SingleOrDefaultAsync(row => row.ModuleId == moduleId, cancellationToken);
        if (floor is null)
        {
            _context.ModuleVersionFloors.Add(new ModuleVersionFloor(moduleId, version, now));
        }
        else
        {
            floor.Raise(version, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return staged;
    }

    /// <summary>Records that the panel started after staging and composed the module's staged package.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="version">The version the loader reports as composed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>True when a staged row of that version existed and is now active; false when there was nothing to mark.</returns>
    public async Task<bool> RecordActivatedAsync(string moduleId, string version, CancellationToken cancellationToken)
    {
        var staged = await _context.InstalledPackages
            .Where(package => package.ModuleId == moduleId
                && package.Version == version
                && package.State == InstalledPackageState.Staged)
            .OrderByDescending(package => package.InstalledAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (staged is null)
        {
            return false;
        }

        staged.MarkActive(_clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Records that a module was removed. Its floor stays, so an older reinstall is still refused.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>True when an open row was closed; false when the ledger holds no open package for the module.</returns>
    public async Task<bool> RecordRemovalAsync(string moduleId, CancellationToken cancellationToken)
    {
        var current = await FindOpenRowsOf(moduleId)
            .OrderByDescending(package => package.InstalledAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return false;
        }

        current.Remove(_clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Reads the module's newest package that is neither superseded nor removed.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or null when the ledger holds no open package for the module.</returns>
    public async Task<InstalledPackage?> FindCurrentAsync(string moduleId, CancellationToken cancellationToken)
    {
        return await FindOpenRowsOf(moduleId)
            .AsNoTracking()
            .OrderByDescending(package => package.InstalledAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Reads the catalogue name the ledger last kept for a module, whatever became of that row.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The name on the module's newest row that has one, so a removed module and a row written before the
    /// name was kept are still shown by name; null when no row ever kept one.
    /// </returns>
    public async Task<string?> FindDisplayNameAsync(string moduleId, CancellationToken cancellationToken)
    {
        return await _context.InstalledPackages
            .AsNoTracking()
            .Where(package => package.ModuleId == moduleId && package.DisplayName != null)
            .OrderByDescending(package => package.InstalledAt)
            .Select(package => package.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Reads the highest version ever staged for a module.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The floor, or null when the module was never staged here.</returns>
    public async Task<SemanticVersion?> ReadFloorAsync(string moduleId, CancellationToken cancellationToken)
    {
        var floor = await _context.ModuleVersionFloors
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.ModuleId == moduleId, cancellationToken);
        if (floor is null || !SemanticVersion.TryParse(floor.HighestVersionSeen, out var version))
        {
            return null;
        }

        return version;
    }

    /// <summary>Lists the recorded history, newest first.</summary>
    /// <param name="moduleId">Only this module's rows, or null for every module's.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows, newest first; the id breaks a tie so the order is stable.</returns>
    public async Task<IReadOnlyList<InstalledPackage>> ListHistoryAsync(
        string? moduleId,
        CancellationToken cancellationToken)
    {
        var rows = _context.InstalledPackages.AsNoTracking();
        if (moduleId is not null)
        {
            rows = rows.Where(package => package.ModuleId == moduleId);
        }

        return await rows
            .OrderByDescending(package => package.InstalledAt)
            .ThenBy(package => package.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Prepares a catalogue name for the column: trimmed, shortened on a code point, null when blank.</summary>
    /// <param name="displayName">The name the catalogue gave, or null.</param>
    /// <returns>The text to store, or null when there is nothing to keep.</returns>
    private static string? NameToKeep(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        return ColumnText.Fit(displayName.Trim(), InstalledPackage.DisplayNameMaxLength);
    }

    /// <summary>The module's rows that are still open: staged or active.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <returns>A query over the open rows; at most one row exists in a healthy ledger.</returns>
    private IQueryable<InstalledPackage> FindOpenRowsOf(string moduleId)
    {
        return _context.InstalledPackages.Where(package => package.ModuleId == moduleId
            && (package.State == InstalledPackageState.Staged || package.State == InstalledPackageState.Active));
    }
}
