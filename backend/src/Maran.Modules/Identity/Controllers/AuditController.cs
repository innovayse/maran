using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListAuditActionChoices;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Reads the append-only audit journal: who did what, when, and from where (spec §10, "Аудит").
/// </summary>
/// <remarks>
/// Administrators only. The journal names every actor on the server and the address they came
/// from, so it is the one place where a customer reading a page would learn about other tenants —
/// which is why it is behind <see cref="AuthorizationPolicies.AdminOnly"/> rather than the
/// controller-wide authenticated default. It is read-only by construction: entries are written by
/// <c>IAuditWriter</c> from inside the handlers that perform the action, never through HTTP, so
/// there is no route here that could forge or amend one.
/// </remarks>
[Route("api/v1/audit")]
[Tags("Audit")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AuditController : BaseApiController
{
    /// <summary>The message bus the queries travel on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus the query travels on.</param>
    public AuditController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads one page of the journal, newest first unless a sort is named, optionally searched and
    /// narrowed by action, outcome and a window of instants.
    /// </summary>
    /// <param name="query">
    /// Page, page size, search, sort and the <c>action</c>, <c>outcome</c>, <c>from</c> and <c>to</c>
    /// filters, bound from the query string; <c>ListAuditEventsQueryValidator</c> holds each to its
    /// bounds.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The page with the total that matched, or a 400 naming the bound that was broken.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<PagedResult<AuditEventDto>>>(query, cancellationToken));
    }

    /// <summary>Lists the actions the journal holds, named for the caller's language, for the action filter.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The choices, ordered by name.</returns>
    [HttpGet("actions")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditActionChoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActionsAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<AuditActionChoiceDto>>>(
            new ListAuditActionChoicesQuery(), cancellationToken));
    }
}
