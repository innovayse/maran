namespace Maran.Modules.Accounts.Domain;

/// <summary>
/// A plan an <see cref="Account"/> is created against: the resource limits spec §8 requires — disk
/// quota and counts for sites, databases and FTP users. Plans are reference data the panel ships
/// with (seeded by <see cref="Seeders.PlanSeeder"/>) and are read-only from every module's
/// perspective in this pass; nothing here mutates a plan after creation.
/// </summary>
public sealed class Plan
{
    /// <summary>The plan's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The resx key for this plan's human-readable name.</summary>
    public string DisplayNameKey { get; private set; }

    /// <summary>The account's disk quota, in megabytes.</summary>
    public int DiskQuotaMb { get; private set; }

    /// <summary>The maximum number of sites the account may create.</summary>
    public int MaxSites { get; private set; }

    /// <summary>The maximum number of databases the account may create.</summary>
    public int MaxDatabases { get; private set; }

    /// <summary>The maximum number of FTP users the account may create.</summary>
    public int MaxFtpUsers { get; private set; }
    /// <summary>Creates a plan.</summary>
    /// <param name="id">The plan's identity, stable across seeding runs so re-seeding is idempotent.</param>
    /// <param name="displayNameKey">
    /// The resx key resolved (via <see cref="SharedKernel.Interfaces.IErrorTextProvider"/>, in the
    /// request culture) for this plan's human-readable name — the same mechanism
    /// <see cref="Sdk.Contracts.Manifest.DisplayNameKey"/> uses for a module's own name
    /// (rules/architecture.md "The backend owns the data, the SPA renders it"), reused here rather
    /// than inventing a second localization path.
    /// </param>
    /// <param name="diskQuotaMb">The account's disk quota, in megabytes.</param>
    /// <param name="maxSites">The maximum number of sites the account may create.</param>
    /// <param name="maxDatabases">The maximum number of databases the account may create.</param>
    /// <param name="maxFtpUsers">The maximum number of FTP users the account may create.</param>
    public Plan(Guid id, string displayNameKey, int diskQuotaMb, int maxSites, int maxDatabases, int maxFtpUsers)
    {
        Id = id;
        DisplayNameKey = displayNameKey;
        DiskQuotaMb = diskQuotaMb;
        MaxSites = maxSites;
        MaxDatabases = maxDatabases;
        MaxFtpUsers = maxFtpUsers;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Plan()
    {
        DisplayNameKey = string.Empty;
    }

}
