using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One account found on a remote server by a discovery: what it holds, as the source reported it. A read-only
/// record of the source, replaced as a whole by the next discovery; nothing here is acted on until a later
/// step validates it again.
/// </summary>
/// <remarks>
/// The counts and <see cref="DomainsText"/> are kept in step with <see cref="Sites"/> and
/// <see cref="Databases"/> by <see cref="AddSite"/> and <see cref="AddDatabase"/>, the only ways to add to them,
/// so a list can be sorted and searched without reading the children. Text the source reported is shortened to the
/// width of the column that holds it: it is evidence, and a record shortened beats a discovery lost to one
/// over-long name. It carries no account id of the panel's: these are the source's accounts, not tenants.
/// </remarks>
public sealed class DiscoveredAccount
{
    /// <summary>The longest login kept.</summary>
    public const int NameMaxLength = 32;

    /// <summary>The longest home directory kept.</summary>
    public const int HomeMaxLength = 4096;

    /// <summary>The sites of the account, changed only through <see cref="AddSite"/>.</summary>
    private readonly List<DiscoveredSite> _sites = [];

    /// <summary>The databases of the account, changed only through <see cref="AddDatabase"/>.</summary>
    private readonly List<DiscoveredDatabase> _databases = [];

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The server the account was found on.</summary>
    public Guid RemoteServerId { get; private set; }

    /// <summary>The account's login on the source.</summary>
    public string Name { get; private set; }

    /// <summary>The account's home directory on the source.</summary>
    public string Home { get; private set; }

    /// <summary>The space the home directory uses; zero when <see cref="DiskKnown"/> is false.</summary>
    public long DiskBytes { get; private set; }

    /// <summary>Whether the source could measure the home directory.</summary>
    public bool DiskKnown { get; private set; }

    /// <summary>How many sites the account serves.</summary>
    public int SiteCount { get; private set; }

    /// <summary>How many databases the account owns.</summary>
    public int DatabaseCount { get; private set; }

    /// <summary>How many scheduled jobs the account has.</summary>
    public int CronEntryCount { get; private set; }

    /// <summary>How many mailboxes the account holds. Counted and never migrated.</summary>
    public int MailboxCount { get; private set; }

    /// <summary>Every domain and alias of the account's sites, space-joined, for search.</summary>
    public string DomainsText { get; private set; }

    /// <summary>The sites of the account; empty until loaded.</summary>
    public IReadOnlyCollection<DiscoveredSite> Sites
    {
        get { return _sites; }
    }

    /// <summary>The databases of the account; empty until loaded.</summary>
    public IReadOnlyCollection<DiscoveredDatabase> Databases
    {
        get { return _databases; }
    }

    /// <summary>Creates an account with no sites or databases yet.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="remoteServerId">The server the account was found on.</param>
    /// <param name="name">The login on the source.</param>
    /// <param name="home">The home directory on the source.</param>
    /// <param name="diskBytes">The space the home uses, or null when not measured; not negative.</param>
    /// <param name="cronEntryCount">How many scheduled jobs; not negative.</param>
    /// <param name="mailboxCount">How many mailboxes; not negative.</param>
    public DiscoveredAccount(
        Guid id,
        Guid remoteServerId,
        string name,
        string home,
        long? diskBytes,
        int cronEntryCount,
        int mailboxCount)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentOutOfRangeException.ThrowIfNegative(cronEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(mailboxCount);
        if (diskBytes is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(diskBytes.Value);
        }

        Id = id;
        RemoteServerId = remoteServerId;
        Name = ColumnText.Fit(name, NameMaxLength);
        Home = ColumnText.Fit(home, HomeMaxLength);
        DiskKnown = diskBytes is not null;
        DiskBytes = diskBytes ?? 0;
        CronEntryCount = cronEntryCount;
        MailboxCount = mailboxCount;
        DomainsText = string.Empty;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DiscoveredAccount()
    {
        Name = string.Empty;
        Home = string.Empty;
        DomainsText = string.Empty;
    }

    /// <summary>Adds a site the account serves.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="aliases">The aliases.</param>
    /// <param name="backend">How the site is served.</param>
    /// <param name="phpVersion">The PHP version, or null.</param>
    /// <param name="documentRoot">The document root.</param>
    public void AddSite(
        Guid id,
        string domain,
        IEnumerable<string> aliases,
        DiscoveredSiteBackend backend,
        string? phpVersion,
        string documentRoot)
    {
        var site = new DiscoveredSite(id, Id, domain, aliases, backend, phpVersion, documentRoot);
        _sites.Add(site);
        SiteCount = _sites.Count;

        var names = site.AliasesText.Length == 0 ? site.Domain : site.Domain + " " + site.AliasesText;
        DomainsText = DomainsText.Length == 0 ? names : DomainsText + " " + names;
    }

    /// <summary>Adds a database the account owns.</summary>
    /// <param name="id">The new row's identity.</param>
    /// <param name="name">The name on the source.</param>
    /// <param name="sizeBytes">The size, or null when not measured; not negative.</param>
    public void AddDatabase(Guid id, string name, long? sizeBytes)
    {
        _databases.Add(new DiscoveredDatabase(id, Id, name, sizeBytes));
        DatabaseCount = _databases.Count;
    }
}
