using Maran.Modules.Identity.Commands.SaveDashboardLayout;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Queries.GetDashboardLayout;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The signed-in person's own dashboard layout, for every role: which widgets and shortcuts the
/// dashboard draws and in what order. Every action is scoped to the caller's token — there is no
/// route, query or body parameter naming a user, so the endpoint cannot be pointed at somebody
/// else (rules/security.md item 6). The layout is a display preference: it grants nothing, and
/// every widget it names reads through an endpoint that enforces its own permission.
/// </summary>
[Route("api/v1/me/dashboard-layout")]
[Tags("Dashboard layout")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class DashboardLayoutController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DashboardLayoutController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the caller's effective layout: the panel's defaults with the caller's choices applied.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(DashboardLayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetDashboardLayoutQuery(
            SignedInCallerPolicy.Require(CurrentUser).UserId,
            CurrentUser.Caller is AdministratorCaller);
        return ToActionResult(await _bus.InvokeAsync<Result<DashboardLayoutDto>>(query, cancellationToken));
    }

    /// <summary>Replaces the caller's whole layout.</summary>
    /// <remarks>
    /// The server checks the keys' shape, the length of each list and duplicates. Whether a key names
    /// a widget the SPA can draw is the SPA's concern, so a key it does not know is stored and
    /// returned unchanged. Refused while an operator is viewing as the login: a look must not
    /// persist anything for the person being looked at.
    /// </remarks>
    /// <param name="command">The complete layout. Whose it is, and the audit origin, are written here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveDashboardLayoutCommand command,
        CancellationToken cancellationToken)
    {
        command = command with
        {
            UserId = SignedInCallerPolicy.Require(CurrentUser).UserId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
