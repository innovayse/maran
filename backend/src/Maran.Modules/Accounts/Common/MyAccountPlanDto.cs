using Maran.Modules.Accounts.Domain.Entities;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// The plan section of <see cref="MyAccountDto"/>: the limits spec §8 promises the customer, with
/// the name resolved for the request's culture (rules/architecture.md "The backend owns the data,
/// the SPA renders it").
/// </summary>
/// <remarks>
/// A distinct type from <see cref="PlanDto"/> rather than a reuse: <see cref="PlanDto"/> answers
/// "what plans exist to pick from" and carries no id-less nesting concern, while this one is always
/// read embedded inside a caller's own account and carries two limits (<see cref="MaxFtpUsers"/>,
/// <see cref="MaxCronEntries"/>) that the plan-picker list has never needed. Growing one to match
/// the other would make an unrelated screen's field list depend on this one's requirements.
/// </remarks>
/// <param name="DisplayName">
/// The plan's human-readable name, resolved from <see cref="Plan.DisplayNameKey"/> server-side.
/// </param>
/// <param name="DiskQuotaMb">The account's disk quota, in megabytes.</param>
/// <param name="MaxSites">The maximum number of sites the account may create.</param>
/// <param name="MaxDatabases">The maximum number of databases the account may create.</param>
/// <param name="MaxSftpUsers">The maximum number of SFTP logins the account may create.</param>
/// <param name="MaxFtpUsers">The maximum number of FTPS logins the account may create.</param>
/// <param name="MaxCronEntries">The maximum number of cron entries the account may keep in its crontab.</param>
public sealed record MyAccountPlanDto(
    string DisplayName,
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries);
