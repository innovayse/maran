using Maran.Modules.Identity.Commands.RevokeSession;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListSessions;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The caller's own signed-in devices. Every action is scoped to the caller's token — there is no
/// route, query or body parameter naming a user, so the endpoint cannot be pointed at somebody else
/// (rules/security.md item 6).
/// </summary>
[Route("api/v1/sessions")]
[Tags("Sessions")]
[Produces("application/json")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SessionsController : BaseApiController
{
    /// <summary>Fallback recorded when the connection reports no remote address, as in a test host.</summary>
    private const string UnknownIpAddress = "unknown";

    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public SessionsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the caller's live sessions, marking the one making this request.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListSessionsQuery(CurrentUser.UserId, CurrentSessionId());
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<SessionDto>>>(query, cancellationToken));
    }

    /// <summary>Ends one of the caller's sessions.</summary>
    /// <remarks>
    /// A session belonging to somebody else answers 404, not 403: 403 would confirm that the id
    /// exists, which is enough to enumerate other people's devices (rules/testing.md).
    /// </remarks>
    /// <param name="id">The session to end.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RevokeSessionCommand(id, CurrentUser.UserId, CallerIpAddress(), CallerUserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>The session this request's access token was issued against.</summary>
    /// <returns>The session id from the <c>sid</c> claim, or <see cref="Guid.Empty"/> when unreadable.</returns>
    private Guid CurrentSessionId()
    {
        return Guid.TryParse(User.FindFirst(PanelClaimTypes.SessionId)?.Value, out var id) ? id : Guid.Empty;
    }

    /// <summary>The caller's address, as recorded in the audit journal.</summary>
    /// <returns>The remote address, or a marker when the connection reports none.</returns>
    private string CallerIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownIpAddress;
    }

    /// <summary>The caller's user agent, as recorded in the audit journal.</summary>
    /// <returns>The header value, truncated to the column's length.</returns>
    private string CallerUserAgent()
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        return userAgent.Length > 512 ? userAgent[..512] : userAgent;
    }
}
