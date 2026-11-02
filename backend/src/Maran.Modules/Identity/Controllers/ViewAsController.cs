using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Commands.EndViewAs;
using Maran.Modules.Identity.Commands.StartViewAs;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Extensions;
using Maran.Modules.Identity.Queries.GetViewAs;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Viewing the panel as an account's login, and coming back (issues #317, #314). The threat note is
/// <c>docs/superpowers/notes/2026-11-02-view-as-impersonation-threat-note.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refresh cookie is never written or deleted here.</b> It holds the operator's own sign-in
/// before, during and after viewing, and every way out — Back, expiry, revocation — returns the
/// operator through an ordinary refresh of it. A view-as session has no refresh token at all.
/// </para>
/// <para>
/// Every action reads who is asking from the caller's own token; no route, query or body parameter
/// names an operator or a session.
/// </para>
/// </remarks>
[Route("api/v1/view-as")]
[Tags("View as")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ViewAsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ViewAsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reports whether the caller's token is a view-as token, and of what.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ViewAsStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetViewAsQuery(CallerSessionId, CurrentUser.RequirePrincipal().UserId);
        return ToActionResult(await _bus.InvokeAsync<Result<ViewAsStateDto>>(query, cancellationToken));
    }

    /// <summary>
    /// Starts viewing the panel as the login that owns an account, returning that login's view-as
    /// access token. Administrators only; refused from inside another view-as session.
    /// </summary>
    /// <remarks>
    /// An account that does not exist, lies outside the caller's scope, or has no login answers 404,
    /// not 403 (rules/security.md item 6).
    /// </remarks>
    /// <param name="command">The account to view as, bound from the body.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [RefusedWhileViewingAs]
    [ProducesResponseType(typeof(ViewAsSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartAsync([FromBody] StartViewAsCommand command, CancellationToken cancellationToken)
    {
        command = command with
        {
            OperatorUserId = CurrentUser.RequirePrincipal().UserId,
            OperatorSessionId = CallerSessionId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<ViewAsSessionDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Ends the view-as session the caller's token belongs to. The SPA then refreshes with the
    /// operator's own cookie to get the operator's token back.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EndAsync(CancellationToken cancellationToken)
    {
        var command = new EndViewAsCommand(CallerSessionId, CurrentUser.RequirePrincipal().UserId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
