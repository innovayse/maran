using System.Text.Json.Serialization;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Interfaces;
using Maran.Sdk.Contracts;
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
/// <param name="CpuQuotaPercent">The share of CPU time the account may use, in percent of ONE CPU, or no ceiling.</param>
/// <param name="MemoryMaxMb">The most memory the account's processes may hold together, in megabytes, or no ceiling.</param>
/// <param name="MaxProcesses">The most processes and threads the account may run at once, or no ceiling.</param>
/// <param name="Features">
/// The features the plan sells, by name: the full set, always stated. An empty list sells none; a
/// request that leaves it out is refused rather than read as "every feature".
/// </param>
/// <param name="PhpLimits">
/// The largest PHP settings a customer on the plan may choose (issue #138). Optional: left out, the
/// plan sells <c>PhpLimits.Default</c>, the conservative set, so a client that predates the bounds
/// still creates a plan with sane ones.
/// </param>
/// <param name="MailLimits">
/// What an account on the plan may do with mail (issue #177). Optional: left out, the plan sells
/// <c>MailLimits.Default</c>, so a client that predates mail still creates a plan with sane figures.
/// </param>
/// <param name="MaxDnsZones">
/// The most DNS zones an account on the plan may own (issue #178). Optional: left out, the plan sells
/// <c>Plan.DefaultMaxDnsZones</c>. Ignored on a plan a reseller owns, which always holds the default.
/// </param>
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
    ResourceCeilingDto CpuQuotaPercent,
    ResourceCeilingDto MemoryMaxMb,
    ResourceCeilingDto MaxProcesses,
    IReadOnlyList<AccountFeature> Features,
    PhpLimitsDto? PhpLimits = null,
    MailLimitsDto? MailLimits = null,
    int? MaxDnsZones = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IPlanDefinition;
