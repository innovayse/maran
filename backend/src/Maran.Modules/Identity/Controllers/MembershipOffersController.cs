using Maran.Modules.Identity.Commands.AcceptMembershipOffer;
using Maran.Modules.Identity.Commands.DeclineMembershipOffer;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListMyMembershipOffers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The caller's own side of an offer to join somebody's account: read the offers addressed to them,
/// accept one, or decline it.
/// </summary>
/// <remarks>
/// <para>
/// The class-level policy is "any signed-in caller" because the person being offered membership holds
/// only a login, not (yet) any standing towards the offering account. What makes an offer theirs is
/// checked by the handlers: an offer is read, accepted or declined only by the login it was addressed
/// to, and any other id, an expired one and one already resolved answer 404 alike.
/// </para>
/// <para>
/// Every action is refused to a view-as token. Accepting adds a login to an account's team, which is
/// an access path that outlives the look, and it is the person's consent to give: an operator
/// looking at the panel as that person must never be able to give it for them.
/// </para>
/// </remarks>
[Route("api/v1/me/membership-offers")]
[Tags("Membership offers")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class MembershipOffersController : BaseApiController
{
    /// <summary>Dispatches each request to its handler.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated caller.</param>
    /// <param name="bus">Dispatches each request to its handler.</param>
    public MembershipOffersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the open offers addressed to the caller, naming the account and the features offered.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The caller's open, unexpired offers; empty when there are none.</returns>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(IReadOnlyList<MembershipOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<MembershipOfferDto>>>(
            new ListMyMembershipOffersQuery(), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Accepts an offer addressed to the caller, which adds them to the offering account's team.</summary>
    /// <param name="id">The offer's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content once the caller is a member.</returns>
    [HttpPost("{id:guid}/accept")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new AcceptMembershipOfferCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Declines an offer addressed to the caller. Nothing is added to the offering account.</summary>
    /// <param name="id">The offer's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content once the offer is closed.</returns>
    [HttpPost("{id:guid}/decline")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeclineMembershipOfferCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
