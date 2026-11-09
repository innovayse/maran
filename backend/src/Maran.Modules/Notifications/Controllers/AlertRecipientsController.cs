using Maran.Modules.Notifications.Commands.SaveAlertRecipients;
using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Queries.GetAlertRecipients;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Notifications.Controllers;

/// <summary>
/// HTTP surface for the addresses operator alerts are mailed to. Thin by design: binds the request,
/// dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only: where the panel's alerts go is a server-wide decision with no tenant
/// dimension, and the list is personal data of the operator's staff.
/// </remarks>
[Route("api/v1/notifications/alert-recipients")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Notifications")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AlertRecipientsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AlertRecipientsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the alert recipient list.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AlertRecipientsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<AlertRecipientsDto>>(
            new GetAlertRecipientsQuery(), cancellationToken));
    }

    /// <summary>Replaces the alert recipient list.</summary>
    /// <param name="command">
    /// The list to save. Bound straight from the body; its two audit fields are unbindable by
    /// construction (see <see cref="SaveAlertRecipientsCommand"/>), so the stamp below is the only
    /// thing that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A PUT: the request carries the whole list, and repeating it leaves the panel in the same state.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveAlertRecipientsCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result>(stamped, cancellationToken));
    }
}
