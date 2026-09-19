using Maran.Modules.Accounts.Commands.RepairAccountHomeGroups;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.InspectAccountHomeGroups;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for the host's own hosting-account home directory groups: what a repair would
/// change, and the repair (issue #28 item E).
/// Thin by design (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>AdminOnly</c> on the class, not <c>AnyAuthenticated</c>, and the argument mirrors
/// <c>DatabaseGrantsController</c>'s exactly.</b>
/// </para>
/// <para>
/// <i>What it does.</i> The host has one password database and one filesystem, and this operation
/// re-groups every hosting account's home directory at once. It is an operator's maintenance
/// control, not a customer's; a customer given it could re-group directories they do not own.
/// </para>
/// <para>
/// <i>What it discloses.</i> A refused row carries the host's raw account name and home path, and a
/// row is refused exactly when something about it does not match what this panel's own account
/// creation would have produced. So whoever reads this result may read another tenant's account name
/// — the same disclosure <c>DatabaseGrantsController</c>'s remarks accept and confine to an
/// administrator, who on this product already holds every account on the server.
/// </para>
/// <para>
/// <i>Why no query filter stands behind it.</i> Nothing here is tenant-scoped: the subject is the
/// host's own password database and filesystem, which has no notion of a tenant at all. There is
/// therefore no 404-instead-of-403 to fall back on: <b>this policy IS the authorisation.</b>
/// </para>
/// <para>
/// <b>Two actions, and the read is not a convenience.</b> <c>GET</c> reports and changes nothing;
/// <c>POST repair</c> acts, and refuses unless the caller sends back the figure the report gave them
/// (<see cref="RepairAccountHomeGroupsCommand.ExpectedRepairCount"/>).
/// </para>
/// </remarks>
[Route("api/v1/account-home-groups")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Account Home Groups")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AccountHomeGroupsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AccountHomeGroupsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reports what a repair would change on this host, changing nothing.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// The pass an operator reads first, and the only route to the repair below. It is a <c>GET</c>
    /// because it performs no write: the agent's report-only mode classifies every home and sends no
    /// <c>chgrp</c>.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(HomeGroupRepairReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new InspectAccountHomeGroupsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<HomeGroupRepairReportDto>>(query, cancellationToken));
    }

    /// <summary>Performs the repair, provided the host still matches the report the caller read.</summary>
    /// <param name="command">
    /// The figure the report gave the operator. Bound straight from the body — there is no request
    /// type in between — and the two record fields the panel establishes for itself are unbindable by
    /// construction (see <see cref="RepairAccountHomeGroupsCommand"/>), so the stamp below is the only
    /// thing that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A 409 here is the honest answer for a host whose homes moved between the report and this
    /// request: nothing was changed, and the remedy is to read a fresh report rather than to retype
    /// anything.
    /// </remarks>
    [HttpPost("repair")]
    [ProducesResponseType(typeof(HomeGroupRepairReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RepairAsync(
        [FromBody] RepairAccountHomeGroupsCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<HomeGroupRepairReportDto>>(stamped, cancellationToken));
    }
}
