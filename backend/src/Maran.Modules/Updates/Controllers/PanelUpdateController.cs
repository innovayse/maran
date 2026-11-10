using Maran.Modules.Updates.Commands.RequestPanelUpdateCheck;
using Maran.Modules.Updates.Commands.StartPanelUpdate;
using Maran.Modules.Updates.Common;
using Maran.Modules.Updates.Queries.GetPanelUpdate;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Updates.Controllers;

/// <summary>
/// HTTP surface for the panel's own update (issue #382): the facts the "Panel updates" screen shows, and
/// the request that makes the root update service run <c>maran update</c>, and the request that makes the
/// root check service fetch the release manifest (issue #516). Thin by design: binds the
/// request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Administrators only, on both routes.</b> Triggering a root code replacement from a web session is
/// the highest-privilege action the panel exposes; the policy on the class is the authorisation and the
/// SPA hiding its button is advice. A customer, a reseller or an API key is answered 403 and an
/// anonymous caller 401. It is 403 and not 404 because there is no identifier to hide: one panel has one
/// update state. This resource is server-wide, so the tenant IDOR test does not apply.
/// </para>
/// <para>
/// <b>The POSTs take no body.</b> The command holds only members the server establishes, so there is
/// nothing a caller could put in the request that reaches root. CSRF is the panel-wide
/// <c>X-Maran-Request</c> header check on every cookie-bearing state change.
/// </para>
/// </remarks>
[Route("api/v1/panel-update")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Panel Update")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class PanelUpdateController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public PanelUpdateController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the running version, the last check's result and whether an update is in flight.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PanelUpdateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PanelUpdateDto>>(new GetPanelUpdateQuery(), cancellationToken));
    }

    /// <summary>Asks the root update service to update the panel; progress appears in the task list.</summary>
    /// <remarks>
    /// Answers 202 with the task's id: the update runs after the answer, restarts the panel, and ends in
    /// the task, not in this response. The request carries nothing; the release comes only from the
    /// signed manifest the root CLI verifies.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(PanelUpdateRequestedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartAsync(CancellationToken cancellationToken)
    {
        var command = new StartPanelUpdateCommand(ClientIpAddress, CallerUserAgent, CorrelationId ?? string.Empty);
        var result = await _bus.InvokeAsync<Result<PanelUpdateRequestedDto>>(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return new ObjectResult(result.Value) { StatusCode = StatusCodes.Status202Accepted, DeclaredType = typeof(PanelUpdateRequestedDto) };
    }

    /// <summary>Asks the root check service to fetch the signed release manifest and record what it names.</summary>
    /// <remarks>
    /// Answers 202 with no body: the check runs after the answer and its result is what the next read
    /// of <c>GET /api/v1/panel-update</c> shows (<c>checkInProgress</c>, then <c>lastCheckedAt</c> and
    /// <c>available</c>). Rate-limited by its own policy because every accepted request makes the
    /// server contact the release host; the request carries nothing.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("check")]
    [EnableRateLimiting(RateLimitPolicies.UpdateCheck)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CheckAsync(CancellationToken cancellationToken)
    {
        var command = new RequestPanelUpdateCheckCommand(ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result>(command, cancellationToken);

        return result.IsSuccess ? Accepted() : ToActionResult(result);
    }
}
