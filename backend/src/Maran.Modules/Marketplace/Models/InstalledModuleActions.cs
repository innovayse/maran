namespace Maran.Modules.Marketplace.Models;

/// <summary>What can be done with, and what is pending for, one installed module; decided by <c>InstalledModuleReader</c>.</summary>
/// <param name="RestartRequired">Whether the module is staged and waits for a panel restart.</param>
/// <param name="DataKept">Whether the module was removed and its data was kept.</param>
/// <param name="CanRollback">Whether a previous version can be restored.</param>
/// <param name="CanRemove">Whether the module can be removed.</param>
public sealed record InstalledModuleActions(bool RestartRequired, bool DataKept, bool CanRollback, bool CanRemove);
