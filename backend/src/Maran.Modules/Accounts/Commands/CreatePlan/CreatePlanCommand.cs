using System.Text.Json.Serialization;
using Maran.Modules.Accounts.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.CreatePlan;

/// <summary>Defines a new plan an account can be created against.</summary>
/// <remarks>
/// Bound directly by <c>POST /api/v1/plans</c>; there is no separate request type. The two audit
/// fields are the server's to state and carry the guard attributes that make that true
/// (rules/csharp.md "Server-established members of a command").
/// </remarks>
/// <param name="Name">
/// The plan's name, as administrators and customers will read it. Unique across plans, compared
/// without regard to case.
/// </param>
/// <param name="DiskQuotaMb">The account's disk quota, in megabytes.</param>
/// <param name="MaxSites">The maximum number of sites the account may create.</param>
/// <param name="MaxDatabases">The maximum number of databases the account may create.</param>
/// <param name="MaxSftpUsers">The maximum number of SFTP logins the account may create.</param>
/// <param name="MaxFtpUsers">The maximum number of FTPS logins the account may create; zero sells none.</param>
/// <param name="MaxCronEntries">The maximum number of cron entries the account may keep; zero sells none.</param>
/// <param name="MaxPhpWorkersPerPool">The maximum number of php-fpm workers one of the account's pools may run.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record CreatePlanCommand(
    string Name,
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IPlanDefinition;
