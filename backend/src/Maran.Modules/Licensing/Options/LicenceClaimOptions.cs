namespace Maran.Modules.Licensing.Options;

/// <summary>
/// Where the licence screen sends an administrator to claim a licence for this server. Only the address of a
/// page a person opens in their own browser: the panel never contacts it. Bound from the same
/// <c>Licensing</c> section as the file paths and the lease service address.
/// </summary>
public sealed class LicenceClaimOptions
{
    /// <summary>The configuration section the options bind from; shared with the licence and lease paths.</summary>
    public const string SectionName = "Licensing";

    /// <summary>The documented marketplace website, the page where a licence is claimed.</summary>
    public const string DefaultClaimPageBaseUrl = "https://plugins-maran.innovayse.com";

    /// <summary>
    /// The website's origin: <c>https://host[:port]</c> and nothing after it. Checked by <c>PublicUrlRule</c>
    /// when the link is built, not at start-up, so a typo can never stop the panel booting; a value that fails
    /// the rule leaves the screen without a link and with the server id still shown.
    /// </summary>
    public string ClaimPageBaseUrl { get; set; } = DefaultClaimPageBaseUrl;
}
