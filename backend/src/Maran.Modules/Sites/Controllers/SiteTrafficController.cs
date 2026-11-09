using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Queries.GetSiteTraffic;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Sites.Controllers;

/// <summary>
/// HTTP surface for a site's traffic summary (issue #152). Thin by design: binds the request, dispatches
/// through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// A separate controller from <see cref="SitesController"/>, which is already past the size at which a
/// file stops being one unit. Open to any signed-in caller whose plan sells the Sites feature; what they
/// can see is decided by the context's tenant filter, so another customer's site answers 404, never 403.
/// The answer is read from PostgreSQL alone: a customer's request never reaches the agent.
/// </remarks>
[Route("api/v1/sites/{id:guid}")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[RequiresAccountFeature(AccountFeature.Sites)]
[Tags("Sites")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SiteTrafficController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public SiteTrafficController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads what a site's access log counted over a window of whole UTC days.</summary>
    /// <param name="id">The site to read; another customer's site answers 404.</param>
    /// <param name="query">The window, from the query string; absent means the first window the server lists.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("traffic")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(SiteTrafficDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromQuery] GetSiteTrafficQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { SiteId = id };
        return ToActionResult(await _bus.InvokeAsync<Result<SiteTrafficDto>>(query, cancellationToken));
    }
}
