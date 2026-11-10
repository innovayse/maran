using Maran.Modules.Apps.Commands.ControlAppService;
using Maran.Modules.Apps.Commands.InstallApp;
using Maran.Modules.Apps.Commands.UninstallApp;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Queries.ListApps;
using Maran.Modules.Apps.Queries.ListInstallableApps;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Apps.Controllers;

/// <summary>
/// The administrator's door to the app store (issue #517): the apps the server has, the ones its package
/// sources can install, and install, uninstall and service control. Administrator only: no customer,
/// reseller or API key reaches any route.
/// </summary>
/// <remarks>
/// <para>
/// Thin on purpose. Every decision, from whether an id is in the catalogue to whether a site still uses an
/// app, is made by the command or query it dispatches. Install and uninstall answer 202 with the task that
/// carries the work; nothing is applied before the answer.
/// </para>
/// <para>
/// The administrator-only policy exempts the routes from the view-as allow-list: an operator viewing as a
/// login holds a customer token, which this policy refuses, so no <c>ViewAsAccess</c> declaration applies.
/// </para>
/// </remarks>
[Route("api/v1/apps")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Apps")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AppsController : BaseApiController
{
    /// <summary>The panel's message bus.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The caller.</param>
    /// <param name="bus">The panel's message bus.</param>
    public AppsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the apps the server has, is installing or removing, one page at a time.</summary>
    /// <param name="query">Search, filter, sort and paging parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page with the total that matched.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AppDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListAppsQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<AppDto>>>(query, cancellationToken));
    }

    /// <summary>Lists the apps the host's package sources can install, installed ones included.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installable apps.</returns>
    [HttpGet("installable")]
    [ProducesResponseType(typeof(IReadOnlyList<InstallableAppDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetInstallableAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<IReadOnlyList<InstallableAppDto>>>(
                new ListInstallableAppsQuery(),
                cancellationToken));
    }

    /// <summary>Starts installing one app as a task.</summary>
    /// <param name="id">The catalogue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with the task id; an install already running answers with its task.</returns>
    [HttpPost("{id}/install")]
    [ProducesResponseType(typeof(AppOperationAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> InstallAsync(string id, CancellationToken cancellationToken)
    {
        var command = new InstallAppCommand(id, ClientIpAddress, CallerUserAgent);

        return AcceptedOrFailure(await _bus.InvokeAsync<Result<AppOperationAcceptedDto>>(command, cancellationToken));
    }

    /// <summary>Starts removing one app as a task. Refused while a site uses the app.</summary>
    /// <param name="id">The catalogue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>202 with the task id; an app a site uses is 409 with the sites listed under <c>sites</c>.</returns>
    [HttpPost("{id}/uninstall")]
    [ProducesResponseType(typeof(AppOperationAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UninstallAsync(string id, CancellationToken cancellationToken)
    {
        var command = new UninstallAppCommand(id, ClientIpAddress, CallerUserAgent);

        return AcceptedOrFailure(await _bus.InvokeAsync<Result<AppOperationAcceptedDto>>(command, cancellationToken));
    }

    /// <summary>Starts, stops, restarts or reloads the service of one installed app.</summary>
    /// <param name="id">The catalogue id.</param>
    /// <param name="action">The action: <c>start</c>, <c>stop</c>, <c>restart</c> or <c>reload</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service status after the action.</returns>
    [HttpPost("{id}/service/{action}")]
    [ProducesResponseType(typeof(AppServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ControlServiceAsync(
        string id,
        AppServiceAction action,
        CancellationToken cancellationToken)
    {
        var command = new ControlAppServiceCommand(id, action, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<AppServiceDto>>(command, cancellationToken));
    }

    /// <summary>Answers 202 with the accepted operation, or the problem for a refusal.</summary>
    /// <param name="result">The outcome of the command.</param>
    /// <returns>The response.</returns>
    private IActionResult AcceptedOrFailure(Result<AppOperationAcceptedDto> result)
    {
        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return new ObjectResult(result.Value)
        {
            StatusCode = StatusCodes.Status202Accepted,
            DeclaredType = typeof(AppOperationAcceptedDto),
        };
    }
}
