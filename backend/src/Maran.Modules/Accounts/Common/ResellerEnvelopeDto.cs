using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What an administrator sold a reseller, on the wire in both directions: what a screen reads and what
/// an administrator sends to create a reseller or change its envelope. The reseller itself only ever
/// reads it.
/// </summary>
/// <remarks>
/// The ceilings reuse <see cref="ResourceCeilingDto"/>, the shape a plan states its own in, so an
/// unlimited ceiling (no cap on that resource) is the same <c>kind</c> on both sides and is never read
/// off a zero. The rules about what it may hold live in <c>ResellerEnvelopeDtoValidator</c>.
/// </remarks>
/// <param name="MaxAccounts">The most accounts, in any status, the reseller may own.</param>
/// <param name="DiskPoolMb">The most disk, in megabytes, the reseller may promise in total across its accounts.</param>
/// <param name="MaxPlans">The most plans the reseller may own.</param>
/// <param name="MaxDiskQuotaMb">The highest disk quota, in megabytes, one of the reseller's plans may state.</param>
/// <param name="MaxSites">The highest site count one of the reseller's plans may state.</param>
/// <param name="MaxDatabases">The highest database count one of the reseller's plans may state.</param>
/// <param name="MaxSftpUsers">The highest SFTP login count one of the reseller's plans may state.</param>
/// <param name="MaxFtpUsers">The highest FTPS login count one of the reseller's plans may state.</param>
/// <param name="MaxCronEntries">The highest cron entry count one of the reseller's plans may state.</param>
/// <param name="MaxPhpWorkersPerPool">The highest php-fpm worker count per pool one of the reseller's plans may state.</param>
/// <param name="MaxMailboxes">The highest mailbox count one of the reseller's plans may state (issue #177).</param>
/// <param name="MailboxQuotaPoolMib">The highest mailbox quota pool, in mebibytes, one of the reseller's plans may state.</param>
/// <param name="Features">The features the reseller may sell, as the names of the set's members; empty sells none.</param>
/// <param name="CpuQuotaPercent">The CPU ceiling the reseller's plans may not exceed, in percent of one CPU, or no ceiling.</param>
/// <param name="MemoryMaxMb">The memory ceiling the reseller's plans may not exceed, in megabytes, or no ceiling.</param>
/// <param name="MaxProcesses">The process ceiling the reseller's plans may not exceed, or no ceiling.</param>
public sealed record ResellerEnvelopeDto(
    int MaxAccounts,
    long DiskPoolMb,
    int MaxPlans,
    int MaxDiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    int MaxMailboxes,
    int MailboxQuotaPoolMib,
    IReadOnlyList<AccountFeature> Features,
    ResourceCeilingDto CpuQuotaPercent,
    ResourceCeilingDto MemoryMaxMb,
    ResourceCeilingDto MaxProcesses);
