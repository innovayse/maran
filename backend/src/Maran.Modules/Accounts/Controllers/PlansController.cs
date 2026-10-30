using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetPlan;
using Maran.Modules.Accounts.Queries.ListPlanChoices;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for managing the plans a server sells. Thin by design (rules/csharp.md "Controller
/// shape is fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>. No business logic, no data access.
///
/// Administrators only. Defining what a hosting package contains, and what happens to the accounts on
/// it when that changes, is the server owner's decision (spec §8); a customer sees only their own
/// plan's limits, through <c>GET /api/v1/accounts/me</c>.
///
/// Separate from <see cref="AccountsController"/> now that a plan has a lifecycle of its own: this is
/// where a plan is listed, made, changed and retired, and where a picker reads its choices
/// (<c>GET /api/v1/plans/choices</c>).
/// </summary>
[Route("api/v1/plans")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Plans")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class PlansController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public PlansController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Lists one page of plans, each with its limits and how many accounts are on it, searched by name
    /// and sorted.
    /// </summary>
    /// <param name="query">Paging, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListPlansQuery query, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<PlanDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Lists one page of plan names for a picker — the account form and the move-to-plan dialog — so
    /// the caller never has to know or type a plan id (rules/architecture.md "The backend owns the
    /// data, the SPA renders it"), and no picker loads the plan list with its headcounts.
    /// </summary>
    /// <param name="query">Paging, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("choices")]
    [ProducesResponseType(typeof(PagedResult<PlanChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetChoicesAsync(
        [FromQuery] ListPlanChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<PlanChoiceDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads one plan, with its limits and how many accounts are on it.</summary>
    /// <param name="id">The plan to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PlanDto>>(new GetPlanQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Defines a new plan.</summary>
    /// <param name="command">
    /// The plan's name and limits. The command is bound straight from the body — there is no request
    /// type in between — and the two audit fields it also carries are unbindable by construction (see
    /// <see cref="CreatePlanCommand"/>), so the stamp below is the only thing that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreatePlanCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<PlanDto>>(stamped, cancellationToken);
        return ToCreatedActionResult(result, $"/api/v1/plans/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Replaces a plan's name and limits. The new limits apply to every account already on the plan.
    /// </summary>
    /// <param name="id">The plan to edit.</param>
    /// <param name="command">The plan's new name and limits.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A disk quota is never lowered below what an account on the plan already uses: that edit is
    /// refused with 409 and changes nothing. See <c>UpdatePlanCommandHandler</c> for why.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdatePlanCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { PlanId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<PlanDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Deletes a plan. Refused with 409 while any account is on it.</summary>
    /// <param name="id">The plan to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeletePlanCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<bool>>(command, cancellationToken);
        return ToActionResult(result);
    }
}
