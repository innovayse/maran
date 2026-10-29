using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Common;
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
/// Separate from <see cref="AccountsController"/> now that a plan has a lifecycle of its own. That
/// controller keeps <c>GET /api/v1/accounts/plans</c>, which the account-creation form reads to offer
/// a choice; this one is where a plan is made, changed and retired.
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

    /// <summary>Lists every plan, each with its limits and how many accounts are on it.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<PlanDto>>>(new ListPlansQuery(), cancellationToken);
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
