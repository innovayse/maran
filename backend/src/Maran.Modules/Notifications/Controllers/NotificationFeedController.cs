using Maran.Modules.Notifications.Commands.DismissAllNotifications;
using Maran.Modules.Notifications.Commands.DismissNotification;
using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Queries.GetNotificationCount;
using Maran.Modules.Notifications.Queries.ListNotifications;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Notifications.Controllers;

/// <summary>
/// HTTP surface for the administrators' notification feed: the entries behind the bell, how many
/// there are, and dismissing them. Thin by design: binds the request, dispatches through Wolverine,
/// translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only. The feed is the operator's: it names tasks, alerts and backups across every
/// account. Dismissal is per administrator — each call acts on the caller's own sign-in and takes no
/// user from the request — so one administrator clearing the bell leaves it full for the others.
/// There is no "read" state: an entry is either active or dismissed, and the count route is named
/// for what the bell calls it.
/// </remarks>
[Route("api/v1/notifications/feed")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Notifications")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class NotificationFeedController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public NotificationFeedController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads one page of the entries the caller has not dismissed, newest first.</summary>
    /// <param name="query">Page, page size, search, sort and the <c>source</c> filter, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The page with the total that matched.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<NotificationDto>>>(query, cancellationToken));
    }

    /// <summary>Counts the entries the caller has not dismissed, capped at 99 with a flag.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The badge figure.</returns>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(NotificationCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCountAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<NotificationCountDto>>(
            new GetNotificationCountQuery(), cancellationToken));
    }

    /// <summary>Dismisses one entry for the caller. Idempotent: an unknown or already dismissed id is also 204.</summary>
    /// <param name="id">The entry, from the route.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DismissAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DismissNotificationCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Dismisses every entry the caller has not dismissed, for the caller only.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("dismiss-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DismissAllAsync(CancellationToken cancellationToken)
    {
        var command = new DismissAllNotificationsCommand(ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
