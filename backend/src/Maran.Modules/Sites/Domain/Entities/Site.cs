using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>
/// A website served from one account's home directory: a domain, the aliases answering with it, the
/// backend that renders it, and whether it is currently serving (spec §11).
/// </summary>
/// <remarks>
/// The agent keeps no record of any of this — the vhost on disk is a RENDERING of this row, not a
/// second copy of it — so this entity is the site's only definition, and every agent call that
/// re-renders a vhost is handed these facts. That is also why nothing here has a public setter: a
/// field assigned from outside is a field that can disagree with what was rendered.
/// </remarks>
public sealed class Site
{
    /// <summary>The hostnames this site claims across the server: its domain and every alias.</summary>
    /// <remarks>
    /// The backing field EF Core reads and writes, and the only place the claims are built. Kept
    /// private so that no caller can claim a name without a site or leave a site without its
    /// claims: <see cref="SiteHostname"/> explains why an unclaimed alias is a domain takeover.
    /// </remarks>
    private readonly List<SiteHostname> _hostnames = [];

    /// <summary>The site's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns this site. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The primary domain served by this site, unique across the server.</summary>
    public string Domain { get; private set; }

    /// <summary>Additional hostnames answered by the same vhost.</summary>
    public IReadOnlyList<string> Aliases { get; private set; }

    /// <summary>
    /// Every hostname this site claims — <see cref="Domain"/> and each of <see cref="Aliases"/> —
    /// as rows a unique key makes exclusive across the whole server.
    /// </summary>
    /// <remarks>
    /// Not a second copy of the two properties above for a reader's convenience: those are what the
    /// vhost is RENDERED from, and this is what a name being taken is DECIDED from, which has to be
    /// a key in the database because a check in a handler cannot be atomic with the insert that
    /// follows it (<see cref="SiteHostname"/>).
    /// </remarks>
    public IReadOnlyCollection<SiteHostname> Hostnames
    {
        get
        {
            return _hostnames;
        }
    }

    /// <summary>Which backend serves this site's content.</summary>
    public SiteBackendType BackendType { get; private set; }

    /// <summary>The bound PHP version, or the empty string when the backend is not PHP.</summary>
    public string PhpVersion { get; private set; }

    /// <summary>The upstream forwarded to, or the empty string when the backend is not a reverse proxy.</summary>
    public string ProxyUpstream { get; private set; }

    /// <summary>The absolute document root the agent allocated. Operator-facing; never rendered into a customer's error.</summary>
    public string DocumentRoot { get; private set; }

    /// <summary>
    /// Whether a TLS certificate is currently installed for this site.
    /// </summary>
    /// <remarks>
    /// NOT dead code, and not to be removed for being always false today: it is the fact every
    /// vhost re-render must be told, because a renderer that guesses drops a live site back to
    /// plain HTTP on the next unrelated edit. Nothing in the Sites module writes it — the Ssl
    /// module owns that write path and flips it when a certificate is installed or removed. Until
    /// then it is honestly false, which is the only safe answer while no certificate exists.
    /// </remarks>
    public bool HasCertificate { get; private set; }

    /// <summary>Whether the site serves its own content or a suspension response.</summary>
    public SiteStatus Status { get; private set; }

    /// <summary>The instant the site was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a site in the <see cref="SiteStatus.Enabled"/> state with no certificate.</summary>
    /// <param name="id">The site's identity.</param>
    /// <param name="accountId">The account that owns this site.</param>
    /// <param name="domain">The primary domain served by this site.</param>
    /// <param name="aliases">Additional hostnames answered by the same vhost.</param>
    /// <param name="backendType">Which backend serves this site's content.</param>
    /// <param name="phpVersion">The bound PHP version, or the empty string when the backend is not PHP.</param>
    /// <param name="proxyUpstream">The upstream forwarded to, or the empty string when the backend is not a proxy.</param>
    /// <param name="documentRoot">The absolute document root the agent allocated.</param>
    /// <param name="createdAt">The instant the site was created, taken from <see cref="IClock"/>.</param>
    public Site(
        Guid id,
        Guid accountId,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendType backendType,
        string phpVersion,
        string proxyUpstream,
        string documentRoot,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Domain = domain;
        Aliases = [.. aliases];
        BackendType = backendType;
        PhpVersion = phpVersion;
        ProxyUpstream = proxyUpstream;
        DocumentRoot = documentRoot;
        HasCertificate = false;
        Status = SiteStatus.Enabled;
        CreatedAt = createdAt;

        // Built here and nowhere else: a site that exists has claimed every name it answers for,
        // by construction rather than by a handler remembering to write a second row.
        _hostnames.Add(new SiteHostname(domain, this));
        foreach (var alias in Aliases)
        {
            _hostnames.Add(new SiteHostname(alias, this));
        }
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Site()
    {
        Domain = string.Empty;
        Aliases = [];
        PhpVersion = string.Empty;
        ProxyUpstream = string.Empty;
        DocumentRoot = string.Empty;
    }

    /// <summary>Rebinds the site to a different installed PHP version.</summary>
    /// <param name="version">The installed version to bind to.</param>
    public void ChangePhpVersion(string version)
    {
        PhpVersion = version;
    }

    /// <summary>Returns the site to serving its own content. Idempotent: enabling an enabled site is a no-op.</summary>
    public void Enable()
    {
        Status = SiteStatus.Enabled;
    }

    /// <summary>Makes the site serve a suspension response. Idempotent: disabling a disabled site is a no-op.</summary>
    public void Disable()
    {
        Status = SiteStatus.Disabled;
    }

    /// <summary>Records that a TLS certificate is now installed for this site.</summary>
    /// <remarks>
    /// The write path <see cref="HasCertificate"/> describes. It exists here, on the entity, because
    /// the Ssl module (which owns certificates) may not reach into this module's rows any other way,
    /// and because a field with no way to become true is a field no test can hold the renderer to.
    /// Nothing in the Sites module calls it; Task 14's certificate installation does.
    /// </remarks>
    public void AttachCertificate()
    {
        HasCertificate = true;
    }

    /// <summary>Records that this site no longer has a TLS certificate.</summary>
    /// <remarks>The other half of <see cref="AttachCertificate"/>; removal must be expressible too, or a
    /// revoked certificate would leave the vhost rendering a TLS block for a file that is gone.</remarks>
    public void DetachCertificate()
    {
        HasCertificate = false;
    }
}
