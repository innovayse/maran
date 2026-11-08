using Maran.Modules.Monitoring.Commands.SaveAlertThresholds;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetAlertThresholds;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Monitoring.Controllers;

/// <summary>
/// HTTP surface for the thresholds at which the panel alarms about the disk, the processor and the
/// memory (issue #142). Thin by design: binds the request, dispatches through Wolverine, translates
/// the <see cref="Result"/> or <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only. The thresholds decide when the operator is told something is wrong with the
/// server every customer shares, so they are server-wide with no tenant dimension, and no API key or
/// reseller reaches them. A customer is answered 403 and an anonymous caller 401.
/// </remarks>
[Route("api/v1/monitoring/alert-thresholds")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Monitoring")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AlertThresholdsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AlertThresholdsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the thresholds in force and the bounds a threshold may take.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AlertThresholdsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetAlertThresholdsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<AlertThresholdsDto>>(query, cancellationToken));
    }

    /// <summary>Replaces all three thresholds.</summary>
    /// <remarks>
    /// A PUT: there is one set of thresholds on a panel, and repeating the same body leaves the panel
    /// in the same state.
    /// </remarks>
    /// <param name="command">The thresholds. The caller's address and user agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveAlertThresholdsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
