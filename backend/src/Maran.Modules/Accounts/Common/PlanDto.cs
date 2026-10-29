using Maran.Modules.Accounts.Domain.Entities;

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
/// <param name="AccountCount">
/// How many accounts are on this plan right now. It is what an administrator needs to see before
/// editing a plan (an edit reaches every one of them) or deleting it (a plan with accounts cannot be
/// deleted), so the screen does not have to fetch every account and count them itself.
/// </param>
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
    int AccountCount);
