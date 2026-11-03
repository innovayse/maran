using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Domain.ValueObjects;

/// <summary>
/// What the administrator sold a reseller: how many accounts and plans it may own, how much disk its
/// accounts may be promised in total, and the ceilings every plan it defines must stay inside.
/// </summary>
/// <remarks>
/// <para>
/// It is the commercial limit of the reseller, so it is set and changed only by an administrator and
/// shown read-only to the reseller. The arithmetic over it lives in one place,
/// <see cref="Policies.ResellerEnvelopePolicy"/>; this type only states the figures and refuses
/// nonsense ones.
/// </para>
/// <para>
/// Every count and ceiling is a non-negative number. Zero is allowed on purpose: an envelope of zero
/// plans sells no plans, which is a product an administrator may genuinely choose. The ceilings of
/// <see cref="RuntimeLimits"/> keep their own meaning: an unlimited one is no ceiling, and a plan
/// under it may then state any figure, including none.
/// </para>
/// </remarks>
public sealed record ResellerEnvelope
{
    /// <summary>The most accounts, in any status, the reseller may own.</summary>
    public int MaxAccounts { get; private set; }

    /// <summary>
    /// The most disk, in megabytes, the reseller may promise in total: the sum of the plan disk quota
    /// of every account it owns.
    /// </summary>
    public long DiskPoolMb { get; private set; }

    /// <summary>The most plans the reseller may own.</summary>
    public int MaxPlans { get; private set; }

    /// <summary>The highest disk quota, in megabytes, one of the reseller's plans may state.</summary>
    public int MaxDiskQuotaMb { get; private set; }

    /// <summary>The highest site count one of the reseller's plans may state.</summary>
    public int MaxSites { get; private set; }

    /// <summary>The highest database count one of the reseller's plans may state.</summary>
    public int MaxDatabases { get; private set; }

    /// <summary>The highest SFTP login count one of the reseller's plans may state.</summary>
    public int MaxSftpUsers { get; private set; }

    /// <summary>The highest FTPS login count one of the reseller's plans may state.</summary>
    public int MaxFtpUsers { get; private set; }

    /// <summary>The highest cron entry count one of the reseller's plans may state.</summary>
    public int MaxCronEntries { get; private set; }

    /// <summary>The highest php-fpm worker count per pool one of the reseller's plans may state.</summary>
    public int MaxPhpWorkersPerPool { get; private set; }

    /// <summary>The features the reseller may sell; every plan it defines must hold a subset of them.</summary>
    public AccountFeature Features { get; private set; }

    /// <summary>
    /// The CPU, memory and process ceilings the reseller's plans may not exceed. An unlimited ceiling
    /// here means no ceiling, so a plan under it may state any figure for that resource.
    /// </summary>
    public RuntimeLimits RuntimeLimits { get; private set; }

    /// <summary>Builds the envelope; reached only through <see cref="Of"/>.</summary>
    /// <param name="maxAccounts">The most accounts the reseller may own.</param>
    /// <param name="diskPoolMb">The most disk, in megabytes, the reseller may promise in total.</param>
    /// <param name="maxPlans">The most plans the reseller may own.</param>
    /// <param name="maxDiskQuotaMb">The highest disk quota one plan may state.</param>
    /// <param name="maxSites">The highest site count one plan may state.</param>
    /// <param name="maxDatabases">The highest database count one plan may state.</param>
    /// <param name="maxSftpUsers">The highest SFTP login count one plan may state.</param>
    /// <param name="maxFtpUsers">The highest FTPS login count one plan may state.</param>
    /// <param name="maxCronEntries">The highest cron entry count one plan may state.</param>
    /// <param name="maxPhpWorkersPerPool">The highest php-fpm worker count per pool one plan may state.</param>
    /// <param name="features">The features the reseller may sell.</param>
    /// <param name="runtimeLimits">The runtime ceilings, already copied so no instance is shared.</param>
    private ResellerEnvelope(
        int maxAccounts,
        long diskPoolMb,
        int maxPlans,
        int maxDiskQuotaMb,
        int maxSites,
        int maxDatabases,
        int maxSftpUsers,
        int maxFtpUsers,
        int maxCronEntries,
        int maxPhpWorkersPerPool,
        AccountFeature features,
        RuntimeLimits runtimeLimits)
    {
        MaxAccounts = maxAccounts;
        DiskPoolMb = diskPoolMb;
        MaxPlans = maxPlans;
        MaxDiskQuotaMb = maxDiskQuotaMb;
        MaxSites = maxSites;
        MaxDatabases = maxDatabases;
        MaxSftpUsers = maxSftpUsers;
        MaxFtpUsers = maxFtpUsers;
        MaxCronEntries = maxCronEntries;
        MaxPhpWorkersPerPool = maxPhpWorkersPerPool;
        Features = features;
        RuntimeLimits = runtimeLimits;
    }

    /// <summary>Parameterless constructor required by EF Core materialization of the owned value.</summary>
    private ResellerEnvelope()
    {
        Features = AccountFeature.None;
        RuntimeLimits = RuntimeLimits.Unlimited();
    }

    /// <summary>States an envelope.</summary>
    /// <param name="maxAccounts">The most accounts the reseller may own.</param>
    /// <param name="diskPoolMb">The most disk, in megabytes, the reseller may promise in total.</param>
    /// <param name="maxPlans">The most plans the reseller may own.</param>
    /// <param name="maxDiskQuotaMb">The highest disk quota one plan may state, in megabytes.</param>
    /// <param name="maxSites">The highest site count one plan may state.</param>
    /// <param name="maxDatabases">The highest database count one plan may state.</param>
    /// <param name="maxSftpUsers">The highest SFTP login count one plan may state.</param>
    /// <param name="maxFtpUsers">The highest FTPS login count one plan may state.</param>
    /// <param name="maxCronEntries">The highest cron entry count one plan may state.</param>
    /// <param name="maxPhpWorkersPerPool">The highest php-fpm worker count per pool one plan may state.</param>
    /// <param name="features">The features the reseller may sell.</param>
    /// <param name="runtimeLimits">The runtime ceilings the reseller's plans may not exceed.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a figure is negative, or <paramref name="features"/> holds a bit that is no feature.
    /// </exception>
    public static ResellerEnvelope Of(
        int maxAccounts,
        long diskPoolMb,
        int maxPlans,
        int maxDiskQuotaMb,
        int maxSites,
        int maxDatabases,
        int maxSftpUsers,
        int maxFtpUsers,
        int maxCronEntries,
        int maxPhpWorkersPerPool,
        AccountFeature features,
        RuntimeLimits runtimeLimits)
    {
        ArgumentNullException.ThrowIfNull(runtimeLimits);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAccounts);
        ArgumentOutOfRangeException.ThrowIfNegative(diskPoolMb);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPlans);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDiskQuotaMb);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSites);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDatabases);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSftpUsers);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFtpUsers);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCronEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPhpWorkersPerPool);

        if (!features.IsKnownSet())
        {
            throw new ArgumentOutOfRangeException(
                nameof(features),
                features,
                "The set holds a bit that is no feature.");
        }

        // The ceilings are rebuilt, not kept: EF Core persists them as owned values, and one instance
        // reachable from two owners is refused at the save.
        var copiedLimits = RuntimeLimits.Of(
            runtimeLimits.CpuQuotaPercent,
            runtimeLimits.MemoryMaxMb,
            runtimeLimits.MaxProcesses);

        return new ResellerEnvelope(
            maxAccounts,
            diskPoolMb,
            maxPlans,
            maxDiskQuotaMb,
            maxSites,
            maxDatabases,
            maxSftpUsers,
            maxFtpUsers,
            maxCronEntries,
            maxPhpWorkersPerPool,
            features,
            copiedLimits);
    }
}
