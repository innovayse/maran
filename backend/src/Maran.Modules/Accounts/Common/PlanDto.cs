namespace Maran.Modules.Accounts.Common;

/// <summary>Outward view of a <see cref="Domain.Plan"/>, with its name resolved for the request's culture.</summary>
/// <param name="Id">The plan's identity.</param>
/// <param name="DisplayName">The plan's human-readable name, resolved server-side (rules/architecture.md "The backend owns the data, the SPA renders it").</param>
/// <param name="DiskQuotaMb">The account's disk quota, in megabytes.</param>
/// <param name="MaxSites">The maximum number of sites the account may create.</param>
/// <param name="MaxDatabases">The maximum number of databases the account may create.</param>
/// <param name="MaxSftpUsers">The maximum number of SFTP logins the account may create.</param>
public sealed record PlanDto(
    Guid Id,
    string DisplayName,
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers);
