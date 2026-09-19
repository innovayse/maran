using Maran.Modules.Databases.Commands.RepairDatabaseGrants;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Queries.InspectDatabaseGrants;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Databases.Controllers;

/// <summary>
/// HTTP surface for the database server's own grant table: what a repair would change, and the repair.
/// Thin by design (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>AdminOnly</c> on the class, not <c>AnyAuthenticated</c>, and the argument has three parts.</b>
/// </para>
/// <para>
/// <i>What it does.</i> One database server has one grant table, and this operation rewrites live
/// access for every customer on the host at once. It is an operator's maintenance control, not a
/// customer's; a customer given it could take away — or be handed — access they do not own.
/// </para>
/// <para>
/// <i>What it discloses.</i> A refused row carries the server's raw <c>Host</c>, <c>Db</c> and
/// <c>User</c> columns, and a row is refused exactly when this panel did not write it. So whoever reads
/// this result reads identifiers they do not own: another tenant's database and user names, or an
/// operator's own hand-made credential. That is the first host-wide listing of such names in the panel
/// — <c>ListDatabases</c> returns names per account — and the alternative, a count with no names, is a
/// refusal nobody can follow up (docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md, "What a
/// second reviewer must check", item 5). The disclosure is accepted and confined to an administrator,
/// who on this product is the person who already holds every account on the server.
/// </para>
/// <para>
/// <i>Why no query filter stands behind it.</i> Nothing here is tenant-scoped, because the subject is
/// not the panel's rows — it is the server's grant table, which has no notion of a tenant at all. There
/// is therefore no 404-instead-of-403 to fall back on and no global filter to catch a careless rewrite:
/// <b>this policy IS the authorisation.</b> Weakening it to <c>AnyAuthenticated</c> would not narrow the
/// answer, it would publish the whole host's grants.
/// </para>
/// <para>
/// <b>Two actions, and the read is not a convenience.</b> <c>GET</c> reports and changes nothing;
/// <c>POST repair</c> acts, and refuses unless the caller sends back the figure the report gave them
/// (<see cref="RepairDatabaseGrantsCommand.ExpectedRepairCount"/>). A single unconditional button was
/// considered and rejected: it would let an administrator rewrite every customer's database access in one
/// click, having seen nothing, with the panel unable to tell afterwards what it had changed from what it
/// had refused.
/// </para>
/// </remarks>
[Route("api/v1/database-grants")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Database Grants")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class DatabaseGrantsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DatabaseGrantsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reports what a repair would change on this host, changing nothing.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// The pass an operator reads first, and the only route to the repair below. It is a <c>GET</c>
    /// because it performs no write on the database server: the agent's report-only mode classifies
    /// every row and sends no statement.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(GrantRepairReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new InspectDatabaseGrantsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<GrantRepairReportDto>>(query, cancellationToken));
    }

    /// <summary>Performs the repair, provided the host still matches the report the caller read.</summary>
    /// <param name="command">
    /// The figure the report gave the operator. Bound straight from the body — there is no request type
    /// in between — and the two record fields the panel establishes for itself are unbindable by
    /// construction (see <see cref="RepairDatabaseGrantsCommand"/>), so the stamp below is the only thing
    /// that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A 409 here is the honest answer for a host whose grants moved between the report and this request:
    /// nothing was changed, and the remedy is to read a fresh report rather than to retype anything.
    /// </remarks>
    [HttpPost("repair")]
    [ProducesResponseType(typeof(GrantRepairReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RepairAsync(
        [FromBody] RepairDatabaseGrantsCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<GrantRepairReportDto>>(stamped, cancellationToken));
    }
}
