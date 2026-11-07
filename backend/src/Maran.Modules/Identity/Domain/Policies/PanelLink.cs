namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The one place a link the panel hands out is put together: the panel's configured public origin
/// followed by a root-relative path. Every link-producing service of the module — the invitation
/// link, the password-reset link, and the invitation mail — calls it, so a link cannot be built two
/// ways and the origin cannot be taken from anywhere else.
/// </summary>
/// <remarks>
/// <b>The origin is only ever the configured one.</b> It is never read from a request's <c>Host</c> or
/// <c>X-Forwarded-Host</c> header, which the caller controls (see
/// <see cref="Maran.Modules.Identity.Options.PanelOptions"/>). The configured value has already been
/// held to <see cref="Maran.SharedKernel.Utilities.Network.PublicUrlRule"/> at start-up, so this
/// type only joins; it does not re-judge.
/// </remarks>
public static class PanelLink
{
    /// <summary>Whether a public origin is configured, so a link can be absolute.</summary>
    /// <param name="publicUrl">The configured <see cref="Maran.Modules.Identity.Options.PanelOptions.PublicUrl"/>.</param>
    /// <returns>True when the value is not empty or whitespace.</returns>
    public static bool IsConfigured(string? publicUrl)
    {
        return !string.IsNullOrWhiteSpace(publicUrl);
    }

    /// <summary>Joins the panel's origin and a root-relative path.</summary>
    /// <param name="publicUrl">The configured public origin, possibly empty.</param>
    /// <param name="relativePath">The path and query, starting with a slash.</param>
    /// <returns>
    /// The absolute link when an origin is configured, else the path unchanged, so the caller can say
    /// the link has no host rather than present a fragment as a complete address.
    /// </returns>
    public static string Compose(string? publicUrl, string relativePath)
    {
        if (!IsConfigured(publicUrl))
        {
            return relativePath;
        }

        return $"{publicUrl!.TrimEnd('/')}{relativePath}";
    }
}
