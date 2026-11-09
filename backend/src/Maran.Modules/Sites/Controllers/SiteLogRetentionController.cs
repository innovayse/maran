using Maran.Modules.Sites.Commands.ChangeSiteLogRetention;
using Maran.Modules.Sites.Common;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Sites.Controllers;

/// <summary>
/// HTTP surface for how long a site keeps its logs (issue #152). Thin by design: binds the request,
/// dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// A separate controller from <see cref="SitesController"/> for the reason <see cref="SiteTrafficController"/>
/// gives. Another customer's site answers 404, never 403. The change is saved and journalled; the archives
/// themselves are removed by the maintenance pass, so the answer never waits on the agent.
/// </remarks>
[Route("api/v1/sites/{id:guid}")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[RequiresAccountFeature(AccountFeature.Sites)]
[Tags("Sites")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SiteLogRetentionController : BaseApiController
{
    /// <summary>The message bus commands are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands are dispatched through.</param>
    public SiteLogRetentionController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Sets how many days of rotated logs the site keeps.</summary>
    /// <param name="id">The site to change; another customer's site answers 404.</param>
    /// <param name="command">The number of days, from 1 to 14.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("log-retention")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeAsync(
        Guid id,
        [FromBody] ChangeSiteLogRetentionCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { SiteId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        return ToActionResult(await _bus.InvokeAsync<Result<SiteDto>>(command, cancellationToken));
    }
}
