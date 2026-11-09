using Maran.Modules.Accounts.Domain.Entities;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Common;

/// <summary>Outward view of a <see cref="Plan"/>, with its name resolved for the request's culture.</summary>
/// <param name="Id">The plan's identity.</param>
/// <param name="DisplayName">The plan's human-readable name, resolved server-side (rules/architecture.md "The backend owns the data, the SPA renders it").</param>
/// <param name="DiskQuotaMb">The account's disk quota, in megabytes.</param>
/// <param name="MaxSites">The maximum number of sites the account may create.</param>
/// <param name="MaxDatabases">The maximum number of databases the account may create.</param>
/// <param name="MaxSftpUsers">The maximum number of SFTP logins the account may create.</param>
/// <param name="MaxFtpUsers">The maximum number of FTPS logins the account may create; zero sells none.</param>
/// <param name="MaxCronEntries">The maximum number of cron entries the account may keep; zero sells none.</param>
/// <param name="MaxPhpWorkersPerPool">The maximum number of php-fpm workers one of the account's pools may run.</param>
/// <param name="CpuQuotaPercent">The share of CPU time the account may use, in percent of ONE CPU, or no ceiling.</param>
/// <param name="MemoryMaxMb">The most memory the account's processes may hold together, in megabytes, or no ceiling.</param>
/// <param name="MaxProcesses">The most processes and threads the account may run at once, or no ceiling.</param>
/// <param name="Features">
/// The features the plan sells, as the names of the set's members (<c>mail</c>, <c>dns</c>,
/// <c>databases</c>, <c>cron</c>, <c>fileTransfer</c>, <c>backups</c> on the wire), so a client never
/// decodes bits. An empty list is a plan that sells none of them.
/// </param>
/// <param name="AccountCount">
/// How many accounts are on this plan right now. It is what an administrator needs to see before
/// editing a plan (an edit reaches every one of them) or deleting it (a plan with accounts cannot be
/// deleted), so the screen does not have to fetch every account and count them itself.
/// </param>
/// <param name="ResellerId">The reseller that owns the plan, or <c>null</c> for the administrator's own.</param>
/// <param name="ResellerName">The owning reseller's name, or <c>null</c> for the administrator's own.</param>
/// <param name="PhpLimits">The largest PHP settings a customer on the plan may choose (issue #138).</param>
/// <param name="MailLimits">What an account on the plan may do with mail: mailboxes, quotas and send limit (issue #177).</param>
/// <param name="MaxDnsZones">The most DNS zones an account on the plan may own; zero sells none (issue #178).</param>
public sealed record PlanDto(
    Guid Id,
    string DisplayName,
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    ResourceCeilingDto CpuQuotaPercent,
    ResourceCeilingDto MemoryMaxMb,
    ResourceCeilingDto MaxProcesses,
    IReadOnlyList<AccountFeature> Features,
    int AccountCount,
    Guid? ResellerId,
    string? ResellerName,
    PhpLimitsDto PhpLimits,
    MailLimitsDto MailLimits,
    int MaxDnsZones = 0);
