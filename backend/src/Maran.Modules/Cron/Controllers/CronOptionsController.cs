using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Queries.GetCronOptions;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Cron.Controllers;

/// <summary>
/// HTTP surface for the reference data the scheduled-tasks screens select from. Thin by design
/// (rules/csharp.md "Controller shape is fixed"): dispatches through Wolverine and translates the
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Open to any signed-in caller on an account that has the feature, because the lists name no
/// account and no row: they are the same for everyone, which is why this controller has no tenant
/// dimension and no IDOR to answer. The lists are advice; every value sent back is validated again
/// by the endpoint that receives it.
/// </remarks>
[Route("api/v1/cron-options")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Cron")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Cron)]
public sealed class CronOptionsController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public CronOptionsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads every closed set the scheduled-tasks screens select from.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The lists, named for the request's culture.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(CronOptionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<CronOptionsDto>>(new GetCronOptionsQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
