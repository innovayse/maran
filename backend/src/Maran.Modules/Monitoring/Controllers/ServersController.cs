using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.ListServers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Monitoring.Controllers;

/// <summary>
/// HTTP surface for the servers the panel manages, which feeds the shell's server selector
/// (issue #319). Thin by design: binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only: which servers a panel manages is operator knowledge, and a customer lives on
/// one account and never chooses a server. A customer is answered 403 and an anonymous caller 401.
/// Read-only on purpose. There is no switch endpoint because there is one server to address; the
/// fleet work (issue #180) adds the registry, and with it the question of whether choosing a server
/// is a privileged, audited action.
/// </remarks>
[Route("api/v1/servers")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Servers")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ServersController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public ServersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the servers the panel manages.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ServerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListServersQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<ServerDto>>>(query, cancellationToken));
    }
}
