using Maran.Modules.Monitoring.Commands.ChangeLogLevel;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetLogLevel;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Monitoring.Controllers;

/// <summary>
/// HTTP surface for the panel's runtime log level (issue #131). Thin by design: binds the request,
/// dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only. The level decides how much of what every tenant does reaches the panel's
/// log, and how much disk that log takes, so it is a server-wide setting with no tenant dimension.
/// A customer is answered 403 and an anonymous caller 401, as on the rest of the monitoring surface.
/// </remarks>
[Route("api/v1/log-level")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Log Level")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class PanelLogLevelController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public PanelLogLevelController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the level the panel's logger is writing at, and the levels on offer.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(LogLevelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetLogLevelQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<LogLevelDto>>(query, cancellationToken));
    }

    /// <summary>Changes the level, effective for the next line logged and after every restart.</summary>
    /// <remarks>
    /// A PUT: there is one level on a panel, and repeating the same body leaves the panel in the same
    /// state.
    /// </remarks>
    /// <param name="command">The level to apply. The caller's address and user agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeAsync(
        [FromBody] ChangeLogLevelCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }
}
