using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Queries.GetAlertDeliveryStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Notifications.Controllers;

/// <summary>
/// HTTP surface for whether the panel's operator alerts are reaching their destination. Thin by
/// design: dispatches the query and translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>Administrators only: it describes the operator's own alert path.</remarks>
[Route("api/v1/notifications/alert-delivery")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Notifications")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AlertDeliveryController : BaseApiController
{
    /// <summary>The message bus the query is dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus the query is dispatched through.</param>
    public AlertDeliveryController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads how the last operator alerts fared on each channel.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AlertDeliveryStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<AlertDeliveryStatusDto>>(
            new GetAlertDeliveryStatusQuery(), cancellationToken));
    }
}
