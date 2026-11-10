using System.Text.Json.Serialization;
using Maran.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.SaveMigrationMapping;

/// <summary>
/// Maps accounts of a draft job to a plan and an owner (issue #197, phase 2). A field left <c>null</c> keeps what the
/// row holds, so one request can apply a plan to many accounts and another can set one account's owner.
/// </summary>
/// <param name="AccountIds">The job's account rows to change; empty means every account of the job.</param>
/// <param name="PlanId">The plan the accounts are created against, or <c>null</c> to keep.</param>
/// <param name="ResellerId">The reseller that owns them, or <c>null</c> to keep.</param>
/// <param name="ClearReseller">Whether the accounts become the administrator's own instead.</param>
/// <param name="OwnerEmail">The owner's e-mail (one account only), or <c>null</c> to keep.</param>
/// <param name="OwnerLanguage">The owner's language, or <c>null</c> to keep.</param>
/// <param name="PrimaryDomain">The primary domain (one account only), or <c>null</c> to keep.</param>
/// <param name="PullGrant">For a Maran source, the single-use grant (one account only); never returned.</param>
/// <param name="JobId">Server-established: the job, from the route.</param>
/// <param name="IpAddress">Server-established: the caller's address.</param>
/// <param name="UserAgent">Server-established: the caller's user agent.</param>
public sealed record SaveMigrationMappingCommand(
    IReadOnlyList<Guid> AccountIds,
    Guid? PlanId = null,
    Guid? ResellerId = null,
    bool ClearReseller = false,
    string? OwnerEmail = null,
    string? OwnerLanguage = null,
    string? PrimaryDomain = null,
    SensitiveString? PullGrant = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid JobId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
