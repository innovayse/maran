using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Queries.GetStackStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Apps.Controllers;

/// <summary>
/// The one app-store route every signed-in role may read: whether the server is set up to serve sites
/// (issue #517). The SPA branches on it before it shows any screen, an administrator to the first-run setup
/// and everyone else to a "being set up" page. Its own controller because an authorization policy on a
/// class is cumulative with one on an action, so this route cannot share a class with the
/// administrator-only store.
/// </summary>
[Route("api/v1/apps/stack")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Apps")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class StackController : BaseApiController
{
    /// <summary>The panel's message bus.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The caller.</param>
    /// <param name="bus">The panel's message bus.</param>
    public StackController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads whether the server is set up to serve sites and with what.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stack status; it carries no tenant data.</returns>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(StackStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<StackStatusDto>>(new GetStackStatusQuery(), cancellationToken));
    }
}
