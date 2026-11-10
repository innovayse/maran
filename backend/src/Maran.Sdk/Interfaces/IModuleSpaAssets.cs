using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The window onto the screen bundles of packaged modules. The closed module loader implements it
/// from its verified copy of each package; the open panel ships <see cref="Modules.NullModuleSpaAssets"/>,
/// which holds none. The window supplies bytes only: the Host decides whether a path is servable,
/// which content type it carries and which headers go with it, so a package can never choose how
/// the panel answers for it.
/// </summary>
public interface IModuleSpaAssets
{
    /// <summary>Describes the screen bundle of the running version of a packaged module.</summary>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The description, or null when the module is unknown or has no screen bundle.</returns>
    Task<ModuleSpaDescription?> DescribeAsync(string moduleId, CancellationToken ct);

    /// <summary>Opens one file of a module's screen bundle.</summary>
    /// <param name="moduleId">The module's <see cref="Manifest.Id"/>.</param>
    /// <param name="version">The version whose bundle is read.</param>
    /// <param name="relativePath">The file's path under <c>spa/</c>, already checked by the Host to be a plain relative path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file, which the caller disposes; null when no such file exists.</returns>
    Task<ModuleSpaAsset?> OpenAsync(string moduleId, string version, string relativePath, CancellationToken ct);
}
