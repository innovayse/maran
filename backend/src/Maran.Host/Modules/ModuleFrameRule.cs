using Maran.Sdk.Contracts;

namespace Maran.Host.Modules;

/// <summary>
/// The one rule that says whether a module's screen is framed and where its frame lives. The catalogue
/// asks it to decide whether to offer a frame and the frame route asks it before serving a byte, so the
/// two can never disagree: a module is framed only when it came from a package, is active and the bundle
/// on offer belongs to the version that is running.
/// </summary>
public static class ModuleFrameRule
{
    /// <summary>The route prefix every frame lives under.</summary>
    public const string RoutePrefix = "/module-frames/";

    /// <summary>The route template of the frame route: module id, running version, then the file inside the bundle.</summary>
    public const string RouteTemplate = RoutePrefix + "{id}/{version}/{**path}";

    /// <summary>The address of a module's frame.</summary>
    /// <param name="moduleId">The composed module's id.</param>
    /// <param name="version">The composed module's version.</param>
    /// <returns>The frame's folder URL, with its trailing slash.</returns>
    public static string FrameUrlOf(string moduleId, string version)
    {
        return $"{RoutePrefix}{moduleId}/{version}/";
    }

    /// <summary>Decides whether a module is framed and, if so, which bundle is served for it.</summary>
    /// <remarks>
    /// A degraded or failed module is not framed even though its bundle exists: the shell shows the licence
    /// or failure panel instead, so a module the panel no longer serves cannot run code in the operator's
    /// browser. A bundle for another version than the composed one is not framed either - the running
    /// module and its screen must be the same release.
    /// </remarks>
    /// <param name="manifest">The composed module's manifest.</param>
    /// <param name="isPackaged">Whether the module arrived as a package.</param>
    /// <param name="lifecycle">The module's state as the registry reports it.</param>
    /// <param name="described">What the screen-bundle window reports for the module, or null when it holds none.</param>
    /// <returns>The bundle to serve, or null when the module is not framed.</returns>
    public static ModuleSpaDescription? Frame(
        Manifest manifest,
        bool isPackaged,
        ModuleLifecycleState lifecycle,
        ModuleSpaDescription? described)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!isPackaged
            || lifecycle != ModuleLifecycleState.Active
            || described is null
            || !string.Equals(described.Version, manifest.Version, StringComparison.Ordinal))
        {
            return null;
        }

        return described;
    }
}
