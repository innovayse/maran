using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListAuditActionChoices;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Modules.Identity.Queries.ListAuditOutcomeChoices;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// A reseller's view of the audit journal: the entries about the accounts it owns and the entries its
/// own login made, and nothing else of the journal (issue #370). Thin by design: binds, dispatches,
/// translates.
/// </summary>
/// <remarks>
/// <para>
/// A controller of its own, not an action added to <see cref="AuditController"/>, for the reason
/// <c>ResellerController</c> gives: that controller's class-level <c>AdminOnly</c> policy is combined
/// with every action's, never overridden, so no action below it can open a door for a reseller. This
/// one is <c>ResellerOnly</c>, so an administrator is refused and reads the whole journal at
/// <c>GET /api/v1/audit</c>, which answers in the same shape.
/// </para>
/// <para>
/// The queries are the administrator's own; what narrows them is <c>AuditEventScopeReader</c>, which
/// reads the reseller from the caller's token and the accounts it owns from the Accounts module. There
/// is no reseller or account-set parameter to widen the scope with, and an <c>account</c> filter naming
/// an account the reseller does not own answers 404, exactly as for an account that does not exist.
/// The journal is read-only and so is every route here; an administrator viewing as the reseller may
/// read it.
/// </para>
/// </remarks>
[Route("api/v1/audit/reseller")]
[Tags("Reseller Audit")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.ResellerOnly)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Read)]
public sealed class ResellerAuditController : BaseApiController
{
    /// <summary>The message bus the queries travel on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The reseller making the request.</param>
    /// <param name="bus">The message bus the query travels on.</param>
    public ResellerAuditController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads one page of the reseller's part of the journal, newest first unless a sort is named,
    /// optionally searched and narrowed by action, outcome, window, actor and one of its own accounts.
    /// </summary>
    /// <param name="query">
    /// The same page, size, search, sort and filters the administrator's journal takes, bound from the
    /// query string and held to the same bounds by <c>ListAuditEventsQueryValidator</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The page with the total that matched, a 400 naming the bound that was broken, or a 404 for an account it does not own.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<PagedResult<AuditEventDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Lists the actions the reseller's part of the journal holds, named for the caller's language, for
    /// the action filter.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The choices, ordered by name.</returns>
    [HttpGet("actions")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditActionChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActionsAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<AuditActionChoiceDto>>>(
            new ListAuditActionChoicesQuery(), cancellationToken));
    }

    /// <summary>Lists the outcomes the journal can be filtered by, named for the caller's language, for the outcome filter.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The choices, in the order the filter offers them.</returns>
    [HttpGet("outcomes")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditOutcomeChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOutcomesAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<AuditOutcomeChoiceDto>>>(
            new ListAuditOutcomeChoicesQuery(), cancellationToken));
    }
}
