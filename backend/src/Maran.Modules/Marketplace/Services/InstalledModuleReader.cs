using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Reads the modules installed from packages: what the loader says runs, beside the ledger's history of
/// what the panel staged. Makes no marketplace call and needs no switch, so the installed list works with
/// the marketplace off.
/// </summary>
/// <remarks>
/// <para>
/// <b>The loader is the truth, the ledger is history.</b> A module is listed because the loader lists it.
/// When the loader reports a module running at the version the ledger still has as staged, the restart that
/// activates it has happened; this reader then closes the ledger row as active, so the screen stops asking
/// for a restart. That is the only write a read makes, it is idempotent, and nothing else would ever close
/// the row, because the panel runs no work of its own at start-up.
/// </para>
/// </remarks>
public sealed class InstalledModuleReader
{
    /// <summary>The loader, which knows what is composed.</summary>
    private readonly IModuleLoader _loader;

    /// <summary>The ledger of what the panel staged.</summary>
    private readonly InstalledPackageLedger _ledger;

    /// <summary>What the last update check found withdrawn.</summary>
    private readonly ObservedRevocations _revocations;

    /// <summary>Creates the reader.</summary>
    /// <param name="loader">The loader, which knows what is composed.</param>
    /// <param name="ledger">The ledger of what the panel staged.</param>
    /// <param name="revocations">What the last update check found withdrawn.</param>
    public InstalledModuleReader(IModuleLoader loader, InstalledPackageLedger ledger, ObservedRevocations revocations)
    {
        _loader = loader;
        _ledger = ledger;
        _revocations = revocations;
    }

    /// <summary>Lists the installed modules, ordered by module id.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One entry per module the loader lists.</returns>
    public async Task<IReadOnlyList<InstalledModuleFacts>> ReadAsync(CancellationToken ct)
    {
        var loaded = await _loader.ListAsync(ct);
        var facts = new List<InstalledModuleFacts>(loaded.Count);

        foreach (var module in loaded.OrderBy(entry => { return entry.ModuleId; }, StringComparer.Ordinal))
        {
            var package = await _ledger.FindCurrentAsync(module.ModuleId, ct);

            if (package is { State: InstalledPackageState.Staged }
                && module.State == ModuleLifecycleState.Active
                && string.Equals(package.Version, module.Version, StringComparison.Ordinal)
                && await _ledger.RecordActivatedAsync(module.ModuleId, module.Version, ct))
            {
                package = await _ledger.FindCurrentAsync(module.ModuleId, ct);
            }

            var displayName = await _ledger.FindDisplayNameAsync(module.ModuleId, ct);
            facts.Add(new InstalledModuleFacts(module, package, _revocations.IsRevoked(module.ModuleId), displayName));
        }

        return facts;
    }

    /// <summary>Decides what is pending for an installed module and what can be done with it.</summary>
    /// <param name="facts">What the panel knows about the module.</param>
    /// <returns>
    /// A restart is required when the loader has the module installed but not composed, or the ledger still
    /// has it staged. A removed module keeps its data. Rollback needs a previous version to go back to, and
    /// neither it nor removal applies to a module that is already removed.
    /// </returns>
    public static InstalledModuleActions ActionsOf(InstalledModuleFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var removed = facts.Loaded.State == ModuleLifecycleState.Removed;
        var restartRequired = facts.Loaded.State == ModuleLifecycleState.Installed
            || facts.Package is { State: InstalledPackageState.Staged };

        return new InstalledModuleActions(
            restartRequired,
            removed,
            !removed && facts.Package?.PreviousVersion is not null,
            !removed);
    }
}
