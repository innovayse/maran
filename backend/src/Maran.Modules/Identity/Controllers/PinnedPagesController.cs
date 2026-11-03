using Maran.Modules.Identity.Commands.PinPage;
using Maran.Modules.Identity.Commands.UnpinPage;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Queries.ListPinnedPages;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The signed-in person's own pinned pages, for every role. Every action is scoped to the caller's
/// token — there is no route, query or body parameter naming a user, so the endpoint cannot be
/// pointed at somebody else (rules/security.md item 6).
/// </summary>
[Route("api/v1/me/pinned-pages")]
[Tags("Pinned pages")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class PinnedPagesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public PinnedPagesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the caller's pinned pages, lowest position first, with the limit.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PinnedPagesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListPinnedPagesQuery(SignedInCallerPolicy.Require(CurrentUser).UserId);
        return ToActionResult(await _bus.InvokeAsync<Result<PinnedPagesDto>>(query, cancellationToken));
    }

    /// <summary>Pins a page for the caller.</summary>
    /// <remarks>
    /// The server checks the key's shape, the per-user limit and duplicates; whether the key names a
    /// page that exists or is offered to the caller's role is the SPA's concern, because the
    /// navigation tree lives in the bundle.
    /// </remarks>
    /// <param name="command">The page to pin. Whose pin it is, and the audit origin, are written here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ViewAsAccess(ViewAsAccess.SafeMutation)]
    [ProducesResponseType(typeof(PinnedPagesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PinAsync([FromBody] PinPageCommand command, CancellationToken cancellationToken)
    {
        command = command with
        {
            UserId = SignedInCallerPolicy.Require(CurrentUser).UserId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };
        return ToActionResult(await _bus.InvokeAsync<Result<PinnedPagesDto>>(command, cancellationToken));
    }

    /// <summary>Removes a page from the caller's pins.</summary>
    /// <param name="itemKey">The navigation tree key of the page to unpin.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{itemKey}")]
    [ViewAsAccess(ViewAsAccess.SafeMutation)]
    [ProducesResponseType(typeof(PinnedPagesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnpinAsync(string itemKey, CancellationToken cancellationToken)
    {
        var command = new UnpinPageCommand(itemKey, SignedInCallerPolicy.Require(CurrentUser).UserId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<PinnedPagesDto>>(command, cancellationToken));
    }
}
