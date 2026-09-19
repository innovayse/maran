using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.RepairAccountHomeGroups;

/// <summary>
/// Re-groups every hosting account's home directory to the web server's group where it is not
/// already that group (issue #28 item E, README's "manual `chgrp`" gap).
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no name, because it acts on accounts nobody asked about.</b> Every hosting account on
/// the host, at once — which is why the endpoint is administrator-only and why the panel will not run
/// it on figures the caller has not read. This mirrors
/// <c>Databases.Commands.RepairDatabaseGrants.RepairDatabaseGrantsCommand</c> exactly, for the reason
/// stated on <c>AccountHomeGroupsController</c>: the same report/confirm shape answers the same
/// question — "an operation that rewrites something live for every customer on the host at once must
/// not run on a figure nobody has read."
/// </para>
/// <para>
/// <b><see cref="ExpectedRepairCount"/> is what makes the inspection the only way in.</b> The handler
/// classifies every home itself before it changes anything and refuses when the number of homes it
/// would re-group is not the number the caller is confirming.
/// </para>
/// </remarks>
/// <param name="ExpectedRepairCount">
/// How many homes the report the operator read said would be re-grouped. Must equal what the host
/// reports at the moment of the repair, or nothing is changed.
/// <para>
/// A signed count, so that a negative value is a VALIDATION failure the caller is told about rather
/// than a figure that silently fails to match and is answered as a stale report.
/// </para>
/// </param>
/// <param name="IpAddress">
/// The caller's address, for the operator-facing record of who ran this. Established by the server and
/// stamped by the action; never bound from the request.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, for the same record. Established by the server and stamped by the action;
/// never bound from the request.
/// </param>
public sealed record RepairAccountHomeGroupsCommand(
    int ExpectedRepairCount,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
