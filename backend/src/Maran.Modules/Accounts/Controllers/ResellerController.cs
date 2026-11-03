using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetMyReseller;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for a reseller's own view of itself: its envelope and what it owns. Thin by design
/// (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// A controller of its own, not an action added to <see cref="ResellersController"/>, for the reason
/// <see cref="MyAccountController"/> gives: that controller's class-level <c>AdminOnly</c> policy is
/// combined with every action's, never overridden, so no action below it can open a door for a reseller.
/// This one is <c>ResellerOnly</c>, so an administrator is refused and reads a reseller through
/// <c>GET /api/v1/resellers/{id}</c>, which carries the same shape. The reseller is the one the caller's
/// token names; there is no parameter to ask for another.
/// </remarks>
[Route("api/v1/reseller")]
[Authorize(Policy = AuthorizationPolicies.ResellerOnly)]
[Tags("Reseller")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ResellerController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ResellerController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads the reseller the caller is: its standing, the envelope it was sold and what it owns against
    /// it. Read-only, so an administrator viewing the panel as this reseller may call it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("me")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(new GetMyResellerQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
