using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Common;

/// <summary>One module installed from a package, read from the loader and the ledger; makes no marketplace call.</summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The catalogue's name for the module, kept by the ledger when the package was staged so the list needs no marketplace call; the module id only for a module whose ledger rows kept none (a package staged before the name was kept).</param>
/// <param name="Version">The installed version.</param>
/// <param name="Lifecycle">The loader's state for the module, as a camelCase word.</param>
/// <param name="FailureMessage">Why a degraded or failed module is in that state, in the caller's language; otherwise null.</param>
/// <param name="Origin">Where the package came from.</param>
/// <param name="InstalledAt">When the panel staged it, or null when the ledger has no row.</param>
/// <param name="PreviousVersion">The version it replaced, or null.</param>
/// <param name="RestartRequired">Whether the module is staged and waits for a panel restart.</param>
/// <param name="DataKept">Whether the module was removed and its data was kept.</param>
/// <param name="Revoked">Whether the last update check found the module withdrawn; false until a check has run.</param>
/// <param name="CanRollback">Whether a previous version can be restored.</param>
/// <param name="CanRemove">Whether the module can be removed.</param>
public sealed record InstalledModuleDto(
    string Id,
    string DisplayName,
    string Version,
    ModuleLifecycleState Lifecycle,
    string? FailureMessage,
    string Origin,
    DateTimeOffset? InstalledAt,
    string? PreviousVersion,
    bool RestartRequired,
    bool DataKept,
    bool Revoked,
    bool CanRollback,
    bool CanRemove);
