namespace Maran.Host.Modules;

/// <summary>
/// The response headers of a module frame, spelled in this one place. The frame's policy replaces the
/// shell's: the shell forbids being framed and allows its own origin, while a module frame must be
/// framed by the shell, may load only its own files, and may make no request of its own - its data
/// travels over the shell's bridge. Nothing here is supplied by a package.
/// </summary>
public static class ModuleFrameHeaders
{
    /// <summary>
    /// The frame's content security policy: nothing by default, scripts, styles, images and fonts from
    /// the frame's own origin, no network access of its own, and framing by the panel only.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'none'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";

    /// <summary>The cache policy of the generated document, which names the entry file and must never be reused stale.</summary>
    public const string DocumentCacheControl = "no-store";

    /// <summary>
    /// The cache policy of a bundle file. The URL carries the module version, so a file never changes
    /// under its URL and may be kept for a year.
    /// </summary>
    public const string AssetCacheControl = "public, max-age=31536000, immutable";

    /// <summary>Writes the headers of the generated document.</summary>
    /// <param name="headers">The response headers.</param>
    public static void ApplyToDocument(IHeaderDictionary headers)
    {
        Apply(headers, DocumentCacheControl);
    }

    /// <summary>Writes the headers of a bundle file.</summary>
    /// <param name="headers">The response headers.</param>
    public static void ApplyToAsset(IHeaderDictionary headers)
    {
        Apply(headers, AssetCacheControl);
    }

    /// <summary>Writes the headers every successful frame answer carries.</summary>
    /// <param name="headers">The response headers.</param>
    /// <param name="cacheControl">The cache policy of this kind of answer.</param>
    private static void Apply(IHeaderDictionary headers, string cacheControl)
    {
        // Overwrites the shell's policy that the pipeline set on every response (UseSecurityHeaders).
        headers.ContentSecurityPolicy = ContentSecurityPolicy;

        // A sandboxed frame has an opaque origin, so its script load is cross-origin and Subresource
        // Integrity requires the response to allow it. The files are signed and not secret.
        headers.AccessControlAllowOrigin = "*";
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = cacheControl;

        // No X-Frame-Options: it cannot express "the panel's own origin" and would contradict frame-ancestors.
        headers.Remove("X-Frame-Options");
    }
}
