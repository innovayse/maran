using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// Serves the screen bundle of a packaged module to its sandboxed frame:
/// <c>GET /module-frames/{id}/{version}/{**path}</c>. The frame has an opaque origin and sends no
/// credentials, so the route is anonymous and rate limited; what makes that safe is that it serves only
/// what the signed package holds, only for the version running, only for a module that is active, and only
/// through the allow-list of <see cref="ModuleFramePathRule"/> and the headers of
/// <see cref="ModuleFrameHeaders"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every unknown is a 404, never a 403.</b> An unknown module, another version than the one running, a
/// module that did not come from a package, one that is not active, a path off the allow-list and a file
/// the package does not hold all answer the same empty 404, so the route tells a stranger nothing about
/// which modules exist or in which state.
/// </para>
/// <para>
/// <b>The folder URL is the panel's document.</b> An empty path answers <see cref="ModuleFrameDocument"/>,
/// generated from the bundle's description; a package's own markup is never served (the extension
/// allow-list has no document type), so what runs in the frame is the entry script and nothing else.
/// </para>
/// </remarks>
public static class ModuleFrameEndpoint
{
    /// <summary>The media type of the generated document.</summary>
    private const string DocumentContentType = "text/html; charset=utf-8";

    /// <summary>Answers one frame request.</summary>
    /// <param name="id">The module id from the route.</param>
    /// <param name="version">The version from the route; must be the running one.</param>
    /// <param name="path">The file inside the bundle, or empty for the folder URL.</param>
    /// <param name="httpContext">The request, whose response headers are written on a success.</param>
    /// <param name="registry">The composed modules and their state.</param>
    /// <param name="assets">The window onto the bundles of packaged modules.</param>
    /// <param name="cancellationToken">Cancels the read when the caller goes away.</param>
    /// <returns>The document or the file, or an empty 404.</returns>
    public static async Task<IResult> ServeAsync(
        string id,
        string version,
        string? path,
        HttpContext httpContext,
        RuntimeModuleRegistry registry,
        IModuleSpaAssets assets,
        CancellationToken cancellationToken)
    {
        var module = registry.Modules.FirstOrDefault(candidate => { return candidate.Manifest.Id == id; });
        if (module is null || !string.Equals(module.Manifest.Version, version, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var isPackaged = registry.IsPackaged(module);
        var lifecycle = (await registry.ListAsync(cancellationToken))
            .First(registered => { return registered.Id == id; })
            .State;
        var described = isPackaged ? await assets.DescribeAsync(id, cancellationToken) : null;

        var bundle = ModuleFrameRule.Frame(module.Manifest, isPackaged, lifecycle, described);
        if (bundle is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(path))
        {
            return ServeDocument(bundle, httpContext);
        }

        return await ServeFileAsync(bundle, path, httpContext, assets, id, cancellationToken);
    }

    /// <summary>Answers the folder URL with the generated document.</summary>
    /// <param name="bundle">The bundle being framed.</param>
    /// <param name="httpContext">The request, whose response headers are written.</param>
    /// <returns>The document, or a 404 when the description cannot be rendered safely.</returns>
    private static IResult ServeDocument(ModuleSpaDescription bundle, HttpContext httpContext)
    {
        if (!ModuleFrameDocument.TryRender(bundle, out var html))
        {
            return Results.NotFound();
        }

        ModuleFrameHeaders.ApplyToDocument(httpContext.Response.Headers);
        return Results.Content(html, DocumentContentType);
    }

    /// <summary>Answers a file of the bundle.</summary>
    /// <param name="bundle">The bundle being framed.</param>
    /// <param name="path">The requested path inside the bundle.</param>
    /// <param name="httpContext">The request, whose response headers are written.</param>
    /// <param name="assets">The window onto the bundles of packaged modules.</param>
    /// <param name="id">The module id.</param>
    /// <param name="cancellationToken">Cancels the read when the caller goes away.</param>
    /// <returns>The file as a stream the framework disposes after writing, or a 404.</returns>
    private static async Task<IResult> ServeFileAsync(
        ModuleSpaDescription bundle,
        string path,
        HttpContext httpContext,
        IModuleSpaAssets assets,
        string id,
        CancellationToken cancellationToken)
    {
        if (!ModuleFramePathRule.TryParse(path, out var relativePath))
        {
            return Results.NotFound();
        }

        var contentType = ModuleFramePathRule.ContentTypeOf(relativePath)!;
        var asset = await assets.OpenAsync(id, bundle.Version, relativePath, cancellationToken);
        if (asset is null)
        {
            return Results.NotFound();
        }

        ModuleFrameHeaders.ApplyToAsset(httpContext.Response.Headers);
        httpContext.Response.ContentLength = asset.Length;
        return Results.Stream(asset.Content, contentType);
    }
}
