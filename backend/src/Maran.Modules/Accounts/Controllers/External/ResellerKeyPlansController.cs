using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetPlan;
using Maran.Modules.Accounts.Queries.ListPlans;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers.External;

/// <summary>
/// The plans a reseller's API key may read (issue #368): the reseller's own plans with their limits,
/// for an integration that provisions on its behalf.
/// </summary>
/// <remarks>
/// Reached ONLY with the <c>ApiKey</c> scheme and the <see cref="ApiKeyPolicyNames.PlansRead"/> policy,
/// which admits a key a reseller owns and holds that scope. Which plans the key sees is decided below
/// this controller, by <c>AccountsDbContext</c>, which narrows a reseller's key to the plans that
/// reseller owns; another reseller's plan answers 404 exactly as one that does not exist. Read-only:
/// no action dispatches a command.
/// </remarks>
[Route("api/v1/reseller-api/plans")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.PlansRead)]
[Tags("Reseller API")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerKeyPlansController : BaseApiController
{
    /// <summary>The message bus the queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the queries are dispatched through.</param>
    public ResellerKeyPlansController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the key's reseller's plans, each with its limits and how many accounts are on it.</summary>
    /// <param name="query">Paging, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListPlansQuery query, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<PlanDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads one of the key's reseller's plans, with its limits and how many accounts are on it.</summary>
    /// <param name="id">The plan to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PlanDto>>(new GetPlanQuery(id), cancellationToken);
        return ToActionResult(result);
    }
}
