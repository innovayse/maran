using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetServerStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Monitoring.Controllers;

/// <summary>
/// The server facts an API key holding the <c>server.read</c> scope may read (issue #144).
/// </summary>
/// <remarks>
/// A separate controller from <see cref="MonitoringController"/> because authorization metadata at
/// class level is combined with an action's, and this endpoint must accept the <c>ApiKey</c> scheme
/// and NOT the administrator policy. It names the scheme and the scope policy explicitly: a key
/// reaches only endpoints that opt in this way, and an administrator's bearer token does not satisfy
/// the <c>ApiKey</c> scheme, so it is answered 401 here. The data is the host's, not an account's:
/// there is no tenant dimension, and view-as is refused because no customer token can reach it.
/// </remarks>
[Route("api/v1/server/status")]
[Tags("Server")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.ServerRead)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ServerStatusController : BaseApiController
{
    /// <summary>The message bus the query is dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the query is dispatched through.</param>
    public ServerStatusController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the pending update counts and the reboot requirement, live from the agent.</summary>
    /// <remarks>
    /// A member the agent did not measure is <c>null</c>, never zero or <c>false</c>. A failed read is
    /// the agent's own typed failure and no document at all.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ServerStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<ServerStatusDto>>(new GetServerStatusQuery(), cancellationToken));
    }
}
