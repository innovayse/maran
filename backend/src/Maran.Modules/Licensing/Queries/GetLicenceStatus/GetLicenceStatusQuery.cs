using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Licensing.Queries.GetLicenceStatus;

/// <summary>
/// Reads the currently installed licence's three-state status (spec §228), changing nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no identifying parameter, and there is none to take.</b> One installation has one
/// licence status; there is no per-something scope to ask about, the same reason
/// <c>Databases.Queries.InspectDatabaseGrants.InspectDatabaseGrantsQuery</c> takes none.
/// </para>
/// <para>
/// <b><see cref="IpAddress"/> and <see cref="UserAgent"/> exist only for the audit entry</b> this
/// read produces (see <c>Services.LicensingAuditJournal</c>'s own remarks for why a read is journalled
/// at all), in the same shape <c>Databases.Commands.RepairDatabaseGrants.RepairDatabaseGrantsCommand</c>
/// carries them: established by the server and stamped by the controller, never bound from the request.
/// </para>
/// <para>
/// This is the read half of licensing, and — per the slice that added it — the ONLY half. Installing
/// or replacing a licence, EF persistence, paid-module gating and the closed <c>PluginLoader</c> seam
/// are explicitly out of scope for this query and its handler; see the module's own log entry.
/// </para>
/// </remarks>
/// <param name="IpAddress">The caller's address, for the audit entry. Stamped by the controller.</param>
/// <param name="UserAgent">The caller's user agent, for the audit entry. Stamped by the controller.</param>
public sealed record GetLicenceStatusQuery(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
