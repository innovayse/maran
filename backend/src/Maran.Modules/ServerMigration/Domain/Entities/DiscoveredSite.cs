using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>One site found on a remote server that belongs to a <see cref="DiscoveredAccount"/>, which creates it.</summary>
public sealed class DiscoveredSite
{
    /// <summary>The longest domain kept: the longest host name DNS allows.</summary>
    public const int DomainMaxLength = 253;

    /// <summary>The longest PHP version text kept.</summary>
    public const int PhpVersionMaxLength = 16;

    /// <summary>The longest document root kept.</summary>
    public const int DocumentRootMaxLength = 4096;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that serves the site.</summary>
    public Guid DiscoveredAccountId { get; private set; }

    /// <summary>The site's primary domain on the source.</summary>
    public string Domain { get; private set; }

    /// <summary>The site's aliases, space-joined (a domain holds no space).</summary>
    public string AliasesText { get; private set; }

    /// <summary>How the site is served.</summary>
    public DiscoveredSiteBackend Backend { get; private set; }

    /// <summary>The PHP version serving it, when known.</summary>
    public string? PhpVersion { get; private set; }

    /// <summary>The directory the site is served from on the source.</summary>
    public string DocumentRoot { get; private set; }

    /// <summary>Creates the row from what the source reported.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="discoveredAccountId">The owning account.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="aliases">The aliases.</param>
    /// <param name="backend">How the site is served.</param>
    /// <param name="phpVersion">The PHP version, or null.</param>
    /// <param name="documentRoot">The document root.</param>
    internal DiscoveredSite(
        Guid id,
        Guid discoveredAccountId,
        string domain,
        IEnumerable<string> aliases,
        DiscoveredSiteBackend backend,
        string? phpVersion,
        string documentRoot)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(documentRoot);

        Id = id;
        DiscoveredAccountId = discoveredAccountId;
        Domain = ColumnText.Fit(domain, DomainMaxLength);
        AliasesText = string.Join(' ', aliases.Select(alias => { return ColumnText.Fit(alias, DomainMaxLength); }));
        Backend = backend;
        PhpVersion = phpVersion is null ? null : ColumnText.Fit(phpVersion, PhpVersionMaxLength);
        DocumentRoot = ColumnText.Fit(documentRoot, DocumentRootMaxLength);
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DiscoveredSite()
    {
        Domain = string.Empty;
        AliasesText = string.Empty;
        DocumentRoot = string.Empty;
    }
}
