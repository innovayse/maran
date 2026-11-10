using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// What the panel knows about one module that arrived as a package: the loader's answer (the truth of what
/// runs) beside the ledger's current row (the history), and whether the last update check found the module
/// withdrawn.
/// </summary>
/// <param name="Loaded">What the loader reports for the module.</param>
/// <param name="Package">The ledger's open row for the module, or null when the ledger holds none.</param>
/// <param name="Revoked">Whether the last update check in this process found the module withdrawn.</param>
/// <param name="DisplayName">The catalogue's name for the module as the ledger kept it, or null when no ledger row kept one.</param>
public sealed record InstalledModuleFacts(
    LoadedModuleInfo Loaded,
    InstalledPackage? Package,
    bool Revoked,
    string? DisplayName);
