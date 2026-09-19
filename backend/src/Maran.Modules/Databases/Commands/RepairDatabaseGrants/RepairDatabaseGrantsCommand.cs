using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Databases.Commands.RepairDatabaseGrants;

/// <summary>
/// Rewrites every grant on this server that this panel issued and whose stored name became a wildcard
/// pattern, narrowing each back to the single database it was meant to name.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no name, because it acts on rows nobody asked about.</b> One database server, one
/// grant table, every customer on the host at once — which is why the endpoint is administrator-only
/// and why the panel will not run it on figures the caller has not read.
/// </para>
/// <para>
/// <b><see cref="ExpectedRepairCount"/> is what makes the inspection the only way in.</b> The handler
/// classifies the table itself before it changes anything and refuses when the number of rows it would
/// rewrite is not the number the caller is confirming. So the value is not a checkbox and not a
/// ceremony: a caller who never read a report has no figure to send, and a caller whose report has gone
/// stale — a database created, a grant added, another administrator having already run the repair —
/// is refused rather than allowed to act on a list that has moved under them.
/// </para>
/// </remarks>
/// <param name="ExpectedRepairCount">
/// How many rows the report the operator read said would be rewritten. Must equal what the server
/// reports at the moment of the repair, or nothing is changed.
/// <para>
/// A signed count, so that a negative value is a VALIDATION failure the caller is told about rather
/// than a figure that silently fails to match and is answered as a stale report. An unsigned one
/// would make those two different mistakes indistinguishable on the wire.
/// </para>
/// </param>
/// <param name="IpAddress">
/// The caller's address, for the operator-facing record of who ran this. Established by the server and
/// stamped by the action; never bound from the request, which is the thing being recorded.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, for the same record. Established by the server and stamped by the action;
/// never bound from the request.
/// </param>
public sealed record RepairDatabaseGrantsCommand(
    int ExpectedRepairCount,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
