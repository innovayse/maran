namespace Maran.Modules.Accounts.Domain.Entities;

/// <summary>
/// A plan an <see cref="Account"/> is created against: the resource limits spec §8 requires — disk
/// quota and counts for sites, databases, SFTP logins and cron entries. Plans are reference data the panel ships
/// with (seeded by <see cref="Seeders.PlanSeeder"/>) and are read-only from every module's
/// perspective in this pass; nothing here mutates a plan after creation.
/// </summary>
/// <remarks>
/// <see cref="MaxSftpUsers"/> was called <c>MaxFtpUsers</c> until the file-transfer allowance was
/// given its module. It is the SAME allowance under a name that now matches what the product ships:
/// this panel serves file transfer over OpenSSH's <c>internal-sftp</c> and installs no FTP daemon at
/// all, so a separate FTP count would be a second limit on one thing — and a plan carrying two of
/// them is a plan where the answer to "how many logins may I have" depends on which module you ask.
/// The column was renamed rather than added beside, so every seeded and operator-edited value
/// carried over unchanged.
/// </remarks>
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

    /// <summary>The maximum number of SFTP logins the account may create.</summary>
    public int MaxSftpUsers { get; private set; }

    /// <summary>The maximum number of cron entries the account may keep in its crontab.</summary>
    /// <remarks>
    /// Counted against the account's CRONTAB rather than against a panel table, because the Cron
    /// module keeps no rows of its own: the crontab is the truth, and the account can edit it
    /// directly over SFTP. A limit counted against a panel copy would be a limit an account could
    /// walk past without the panel ever knowing.
    /// </remarks>
    public int MaxCronEntries { get; private set; }

    /// <summary>The maximum number of concurrent php-fpm workers ONE of the account's pools may run.</summary>
    /// <remarks>
    /// Per POOL, not per account, because that is what it becomes: it is written into every rendered
    /// pool as <c>pm.max_children</c> (spec §8, §11), and an account owns one pool per PHP version
    /// it uses. The distinction is not pedantic — read as a per-account total it would look like a
    /// modest number while handing a 500-site plan five hundred times that many workers.
    ///
    /// This is where a hosting plan's CPU and memory ceiling is actually enforced: a pool with no
    /// bound lets one customer's traffic spike consume the whole server.
    /// </remarks>
    public int MaxPhpWorkersPerPool { get; private set; }

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
    /// <param name="maxDatabases">
    /// The maximum number of databases the account may create. Must be positive: a plan sold with a
    /// zero allowance is a plan on which every creation is refused, and the refusal names the plan
    /// rather than the mistake — so the account looks broken and the plan looks fine. Refused here,
    /// at the one place a plan comes into existence, rather than guessed at by whatever reads it.
    /// </param>
    /// <param name="maxSftpUsers">
    /// The maximum number of SFTP logins the account may create. Must be positive, for the reason
    /// <paramref name="maxDatabases"/> gives: a plan whose allowance is zero refuses every creation
    /// while naming the plan rather than the mistake, so the account looks broken and the plan looks
    /// fine. It matters a shade more here, because an account with no SFTP login has no way to put
    /// files on its own sites at all.
    /// </param>
    /// <param name="maxCronEntries">
    /// The maximum number of cron entries the account may keep. Zero is ACCEPTED here, unlike the
    /// two allowances above, and the difference is not an oversight: an account with no SFTP login
    /// cannot put files on its own sites and a pool with no workers cannot serve PHP, so a zero
    /// there is a broken plan wearing a working plan's name — while "this tier does not include
    /// scheduled tasks" is a product a hosting company may genuinely sell. Negative is still
    /// refused, because it is not a smaller allowance, it is nonsense.
    /// </param>
    /// <param name="maxPhpWorkersPerPool">
    /// The maximum number of concurrent php-fpm workers ONE of the account's pools may run. Must be
    /// positive: it becomes <c>pm.max_children</c>, and php-fpm refuses to start a pool with a
    /// non-positive one, so a zero here is a plan that cannot serve PHP at all.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maxDatabases"/>, <paramref name="maxSftpUsers"/> or
    /// <paramref name="maxPhpWorkersPerPool"/> is not positive, or when
    /// <paramref name="maxCronEntries"/> is negative.
    /// </exception>
    public Plan(
        Guid id,
        string displayNameKey,
        int diskQuotaMb,
        int maxSites,
        int maxDatabases,
        int maxSftpUsers,
        int maxCronEntries,
        int maxPhpWorkersPerPool)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDatabases);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSftpUsers);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCronEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPhpWorkersPerPool);

        Id = id;
        DisplayNameKey = displayNameKey;
        DiskQuotaMb = diskQuotaMb;
        MaxSites = maxSites;
        MaxDatabases = maxDatabases;
        MaxSftpUsers = maxSftpUsers;
        MaxCronEntries = maxCronEntries;
        MaxPhpWorkersPerPool = maxPhpWorkersPerPool;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Plan()
    {
        DisplayNameKey = string.Empty;
    }
}
