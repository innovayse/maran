using System.Text.Json.Serialization;
using Maran.Modules.Accounts.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.UpdatePlan;

/// <summary>
/// Replaces a plan's name and every limit it sells — and with them the limits of every account
/// already on it.
/// </summary>
/// <remarks>
/// Bound directly by <c>PUT /api/v1/plans/{id}</c>. It is a full replacement, not a patch: the screen
/// always holds every field, and a partial update would need a rule for "absent" that is
/// indistinguishable from "zero" for the three counts where zero is a real product. The plan's id
/// sits with the server-established members at the end rather than first, because a guarded
/// non-nullable parameter must carry a default and a C# default is only legal in a trailing position.
/// </remarks>
/// <param name="Name">
/// The plan's name. Sending the name the plan already shows keeps a shipped plan's translated name;
/// sending anything else gives the plan that text, in every language, from then on.
/// </param>
/// <param name="DiskQuotaMb">The account's disk quota, in megabytes. See the handler for what lowering it does.</param>
/// <param name="MaxSites">The maximum number of sites the account may create.</param>
/// <param name="MaxDatabases">The maximum number of databases the account may create.</param>
/// <param name="MaxSftpUsers">The maximum number of SFTP logins the account may create.</param>
/// <param name="MaxFtpUsers">The maximum number of FTPS logins the account may create; zero sells none.</param>
/// <param name="MaxCronEntries">The maximum number of cron entries the account may keep; zero sells none.</param>
/// <param name="MaxPhpWorkersPerPool">The maximum number of php-fpm workers one of the account's pools may run.</param>
/// <param name="PlanId">
/// The plan being edited, established by the server from the route and stamped by the action. Never
/// bound from the request body — a body id could disagree with the path id, and one request would
/// then name two plans.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record UpdatePlanCommand(
    string Name,
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    [property: JsonIgnore][property: BindNever][BindNever] Guid PlanId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IPlanDefinition;
