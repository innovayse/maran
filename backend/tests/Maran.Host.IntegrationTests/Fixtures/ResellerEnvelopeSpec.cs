using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// What a test reseller was sold, written once and read two ways: as the domain value a fixture stores
/// directly, and as the request body an administrator posts. Both readings come from the same figures,
/// so a test that stores an envelope and a test that posts one cannot disagree about what "the default
/// envelope" is.
/// </summary>
/// <remarks>
/// The defaults are chosen so each ceiling can be crossed by exactly one unit in a test and the
/// aggregates have room for exactly the arithmetic the test performs: three accounts, a disk pool of
/// three 5 120 MB plans, three plans, and a per-plan disk ceiling of two 5 120 MB plans. The memory
/// ceiling is unlimited, which sells no cap, so a plan may state any memory figure; the CPU and process
/// ceilings are limited, so a plan may not leave them unlimited.
/// </remarks>
/// <param name="MaxAccounts">How many accounts the reseller may own.</param>
/// <param name="DiskPoolMb">The sum of the disk quotas of the plans the reseller's accounts are on, in MB.</param>
/// <param name="MaxPlans">How many plans the reseller may own.</param>
/// <param name="MaxDiskQuotaMb">The most disk any one plan of the reseller may state, in MB.</param>
/// <param name="MaxSites">The most sites any one plan may allow.</param>
/// <param name="MaxDatabases">The most databases any one plan may allow.</param>
/// <param name="MaxSftpUsers">The most SFTP logins any one plan may allow.</param>
/// <param name="MaxFtpUsers">The most FTPS logins any one plan may allow.</param>
/// <param name="MaxCronEntries">The most cron entries any one plan may allow.</param>
/// <param name="MaxPhpWorkersPerPool">The most PHP workers per pool any one plan may allow.</param>
/// <param name="CpuQuotaPercent">The CPU ceiling any one plan must stay at or under; always limited.</param>
/// <param name="MaxProcesses">The process ceiling any one plan must stay at or under; always limited.</param>
public sealed record ResellerEnvelopeSpec(
    int MaxAccounts = 3,
    long DiskPoolMb = 15_360,
    int MaxPlans = 3,
    int MaxDiskQuotaMb = 10_240,
    int MaxSites = 10,
    int MaxDatabases = 5,
    int MaxSftpUsers = 4,
    int MaxFtpUsers = 2,
    int MaxCronEntries = 3,
    int MaxPhpWorkersPerPool = 8,
    int CpuQuotaPercent = 200,
    int MaxProcesses = 128)
{
    /// <summary>The features every test envelope sells, as the domain's flag set.</summary>
    public const AccountFeature SoldFeatures = AccountFeature.Mail
        | AccountFeature.Dns
        | AccountFeature.Databases
        | AccountFeature.Cron
        | AccountFeature.FileTransfer
        | AccountFeature.Backups;

    /// <summary>The same features as the names the wire carries, in the order the panel lists them.</summary>
    public static readonly string[] SoldFeatureNames = ["mail", "dns", "databases", "cron", "fileTransfer", "backups"];

    /// <summary>The domain value a fixture stores for a reseller.</summary>
    /// <returns>The envelope.</returns>
    public ResellerEnvelope ToDomain()
    {
        return ResellerEnvelope.Of(
            MaxAccounts,
            DiskPoolMb,
            MaxPlans,
            MaxDiskQuotaMb,
            MaxSites,
            MaxDatabases,
            MaxSftpUsers,
            MaxFtpUsers,
            MaxCronEntries,
            MaxPhpWorkersPerPool,
            SoldFeatures,
            RuntimeLimits.Of(
                ResourceLimit.Limited(CpuQuotaPercent),
                ResourceLimit.Unlimited(),
                ResourceLimit.Limited(MaxProcesses)));
    }

    /// <summary>The request body an administrator posts to sell the reseller these figures.</summary>
    /// <returns>An object the JSON serializer writes as the envelope.</returns>
    public object ToBody()
    {
        return new
        {
            MaxAccounts,
            DiskPoolMb,
            MaxPlans,
            MaxDiskQuotaMb,
            MaxSites,
            MaxDatabases,
            MaxSftpUsers,
            MaxFtpUsers,
            MaxCronEntries,
            MaxPhpWorkersPerPool,
            Features = SoldFeatureNames,
            CpuQuotaPercent = new { Kind = "limited", Maximum = CpuQuotaPercent },
            MemoryMaxMb = new { Kind = "unlimited" },
            MaxProcesses = new { Kind = "limited", Maximum = MaxProcesses },
        };
    }
}
