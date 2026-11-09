using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Installs, upgrades, rolls back and unloads modules that arrive as packages. The open panel ships
/// only <see cref="Modules.NullModuleLoader"/>, which loads nothing; a separately distributed package
/// replaces it by registering its own implementation first. Every failure is a <see cref="Result"/>,
/// never an exception: a refused package is data the operator reads.
/// </summary>
public interface IModuleLoader
{
    /// <summary>Installs a module that is not installed yet.</summary>
    /// <param name="source">Where the package comes from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was installed, or the reason the package was refused.</returns>
    Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct);

    /// <summary>Replaces an installed module with a newer package of it.</summary>
    /// <param name="source">Where the package comes from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What is installed now, or the reason the package was refused; the previous version stays active then.</returns>
    Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct);

    /// <summary>Returns a module to the version it ran before its last upgrade.</summary>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reason the rollback was refused.</returns>
    Task<Result> RollbackAsync(string moduleId, CancellationToken ct);

    /// <summary>Stops a module and removes its package.</summary>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the reason the module could not be unloaded.</returns>
    Task<Result> UnloadAsync(string moduleId, CancellationToken ct);

    /// <summary>Lists the modules this loader holds.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One entry per module the loader holds; empty when it holds none.</returns>
    Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct);
}
