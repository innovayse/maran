using Maran.Modules.Accounts.Commands.SetResellerBranding;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetResellerBranding;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for the branding a reseller gives its customers' shell: read it, replace it. Thin by
/// design (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// A controller of its own, <c>ResellerOnly</c> like <see cref="ResellerController"/>, because the
/// class-level policy of <see cref="ResellersController"/> (<c>AdminOnly</c>) is combined with every
/// action's and can never be loosened by one. The reseller is the one the caller's token names; there
/// is no parameter to name another. What the customers see is read through
/// <see cref="MyBrandingController"/>.
/// </remarks>
[Route("api/v1/reseller/branding")]
[Authorize(Policy = AuthorizationPolicies.ResellerOnly)]
[Tags("Reseller")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ResellerBrandingController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ResellerBrandingController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads the branding the caller's customers see, for the settings form. Every member is empty when
    /// the reseller set none. Read-only, so an administrator viewing as this reseller may call it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(ResellerBrandingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ResellerBrandingDto>>(
            new GetResellerBrandingQuery(), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Replaces the branding the caller's customers see. All three values are replaced together; a
    /// value left out goes back to the panel's own, and a body with all three left out removes the
    /// branding. Refused while viewing as this reseller: the change outlives the look.
    /// </summary>
    /// <param name="command">
    /// The name, accent colour and logo. The two audit fields it also carries are unbindable by
    /// construction (see <see cref="SetResellerBrandingCommand"/>), so the stamp below is the only thing
    /// that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ResellerBrandingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetAsync(
        [FromBody] SetResellerBrandingCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<ResellerBrandingDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }
}
