namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>
/// Where one ledger row stands in the life of a module package. A row is history: it records what the
/// panel staged and what became of it, and never what is running now (that is the loader's answer).
/// </summary>
public enum InstalledPackageState
{
    /// <summary>The package was verified and kept; the module activates at the next start of the panel.</summary>
    Staged,

    /// <summary>The panel started after the package was staged and composed the module.</summary>
    Active,

    /// <summary>A later row replaced this one: an upgrade, or a rollback to an older package.</summary>
    Superseded,

    /// <summary>The module was removed; its data was kept (the schema stays).</summary>
    Removed,
}
