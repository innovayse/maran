namespace Maran.Modules.Marketplace.Options;

/// <summary>
/// Where the panel finds the marketplace and the bounds it holds the conversation to. Bound from the
/// <c>Marketplace</c> section; every default is the documented one, so a server that sets nothing talks to
/// the documented host (and only after an administrator has switched the marketplace on).
/// </summary>
/// <remarks>
/// <para>
/// <b>Validated lazily, never at start-up.</b> A server that never turns the marketplace on must start
/// whatever <c>panel.env</c> says, so these options carry no <c>ValidateOnStart</c>: the first read of
/// <c>IOptions&lt;MarketplaceOptions&gt;.Value</c> runs
/// <see cref="Maran.Modules.Marketplace.Validators.MarketplaceOptionsValidator"/>, which is the first
/// administrator action that needs the marketplace.
/// </para>
/// <para>
/// <b>The host is only ever the configured one</b> (the first of the four locks): <see cref="BaseUrl"/> is
/// the host allow-list, a redirect or a download address on another host is refused against it, and no
/// request is addressed from anything a response said.
/// </para>
/// </remarks>
public sealed class MarketplaceOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Marketplace";

    /// <summary>The name of the HTTP client the marketplace transport sends through.</summary>
    public const string HttpClientName = "marketplace";

    /// <summary>The documented marketplace address, which is also the default.</summary>
    public const string DefaultBaseUrl = "https://api-marketplace-maran.innovayse.com";

    /// <summary>The documented release channel, which is also the default.</summary>
    public const string DefaultChannel = "stable";

    /// <summary>
    /// The most a downloaded package may weigh, in bytes: the total the package reader accepts unpacked
    /// (256 MiB). Stated here and not read from the packaging library, which a module may not reference; a
    /// package larger than the reader's own ceiling could be downloaded and then refused, so no setting
    /// may allow it.
    /// </summary>
    public const long PackageBytesCeiling = 256L * 1024 * 1024;

    /// <summary>The largest catalogue response any setting may allow, in bytes (16 MiB).</summary>
    public const long CatalogueBytesCeiling = 16L * 1024 * 1024;

    /// <summary>The largest download-grant response any setting may allow, in bytes (1 MiB).</summary>
    public const long GrantBytesCeiling = 1024L * 1024;

    /// <summary>The longest a request may take, in seconds, whatever the setting says.</summary>
    public const int RequestTimeoutSecondsCeiling = 300;

    /// <summary>The longest a signed answer stays fresh, in hours (one week).</summary>
    public const int CatalogueMaxAgeHoursCeiling = 168;

    /// <summary>The longest the catalogue is cached in memory, in minutes.</summary>
    public const int CacheMinutesCeiling = 60;

    /// <summary>The marketplace's address: an absolute https URL with a host and no user information, query or fragment.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Where the pinned public key that signs catalogue answers is read from (absolute path).</summary>
    public string MetadataKeyPath { get; set; } = "/usr/local/maran/keys/marketplace-metadata.pub";

    /// <summary>The panel-owned directory a package is downloaded into (absolute path, outside any account home).</summary>
    public string PackageDirectory { get; set; } = "/var/lib/maran/marketplace";

    /// <summary>The release channel asked for.</summary>
    public string Channel { get; set; } = DefaultChannel;

    /// <summary>The deadline of one request, in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>The most a catalogue response may weigh, in bytes.</summary>
    public long MaxCatalogueBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>The most a download-grant response may weigh, in bytes.</summary>
    public long MaxGrantBytes { get; set; } = 16L * 1024;

    /// <summary>The most a downloaded package may weigh, in bytes; never above <see cref="PackageBytesCeiling"/>.</summary>
    public long MaxPackageBytes { get; set; } = PackageBytesCeiling;

    /// <summary>How old a signed answer may be before it is treated as stale, in hours.</summary>
    public int CatalogueMaxAgeHours { get; set; } = 24;

    /// <summary>How long the catalogue is kept in memory, in minutes.</summary>
    public int CacheMinutes { get; set; } = 10;
}
