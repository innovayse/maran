using Maran.Modules.Identity.Commands.ChangeSubUserGrants;
using Maran.Modules.Identity.Commands.InviteSubUser;
using Maran.Modules.Identity.Commands.OfferMembership;
using Maran.Modules.Identity.Commands.RemoveSubUser;
using Maran.Modules.Identity.Commands.ResendSubUserInvitation;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListGrantableFeatures;
using Maran.Modules.Identity.Queries.ListSubUsers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The people an account's owner added to the account: invite one, resend an invitation, offer
/// membership to a login that already exists, choose which features they may use, and end their access.
/// </summary>
/// <remarks>
/// The class-level policy is "any signed-in caller" because the question this controller asks is not
/// about the panel-wide role but about the caller's standing towards ONE account, and only the handlers
/// can answer it (<c>SubUserOwnerGuard</c>): an owner passes, a team member and an administrator are
/// refused with 403. There is no account id anywhere in a route or body — the account is always the one
/// the caller's token speaks for — so there is nothing to aim at another account's team.
/// </remarks>
[Route("api/v1/me/sub-users")]
[Tags("Sub-users")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class SubUsersController : BaseApiController
{
    /// <summary>Dispatches each request to its handler.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated caller.</param>
    /// <param name="bus">Dispatches each request to its handler.</param>
    public SubUsersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the people the caller added to their account.</summary>
    /// <param name="query">Page, size, search and sort.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of sub-users.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SubUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListSubUsersQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<SubUserDto>>>(query, cancellationToken));
    }

    /// <summary>Lists the features an owner may give a sub-user, named in the caller's language.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every grantable feature with its label.</returns>
    [HttpGet("features")]
    [ProducesResponseType(typeof(IReadOnlyList<GrantableFeatureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFeaturesAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<GrantableFeatureDto>>>(
            new ListGrantableFeaturesQuery(), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Asks for a person to be added to the caller's account and invited by mail. Rate limited by the
    /// invitation-SENDING policy (per account), which is neither the general one nor the acceptance
    /// bucket: this is the one action that makes the panel send mail to an address the caller typed,
    /// so it must not be a way to send a stranger a stream of mail, and it must not share a meter
    /// with people accepting their own invitations.
    /// </summary>
    /// <remarks>
    /// Answers <c>202 Accepted</c> with the same body whether or not the login name or address was
    /// already in use anywhere on the server: that is not the caller's to learn (issue #341). The
    /// person appears in the list when the invitation was created.
    /// </remarks>
    /// <param name="command">Their login name, address and features.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request as accepted — an echo of the caller's own input.</returns>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.InvitationSending)]
    [ProducesResponseType(typeof(SubUserInvitationDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InviteAsync(
        [FromBody] InviteSubUserCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<SubUserInvitationDto>>(command, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : ToActionResult(result);
    }

    /// <summary>
    /// Sends the invitation of a team member who has not accepted it yet again, to the address already
    /// stored on that login. Rate limited by the same per-account sending bucket as an invitation and
    /// an offer: it is a way to make the panel send mail, and it must not become a way to send one
    /// address a stream of it.
    /// </summary>
    /// <remarks>
    /// The address is never part of the request. The previous link stops working the moment the new one
    /// is minted, so at most one link is live. A login that has already accepted answers 409
    /// <c>SubUserNotPending</c>, which tells the owner only about their own team member.
    /// </remarks>
    /// <param name="id">The invited login's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content once a new invitation has been sent.</returns>
    [HttpPost("{id:guid}/invitation")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [EnableRateLimiting(RateLimitPolicies.InvitationSending)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendInvitationAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ResendSubUserInvitationCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>
    /// Offers membership of the caller's account to a login that already exists. The person is asked
    /// inside their own signed-in session and is added only if they accept. Rate limited by the
    /// per-account invitation-SENDING policy, shared with an invitation and a resend.
    /// </summary>
    /// <remarks>
    /// Answers <c>202 Accepted</c> with the same body whether or not the identifier named a login that
    /// can be offered anything: that is not the caller's to learn. The caller's own state (not an
    /// owner, team full, malformed input) is refused before the identifier is looked at.
    /// </remarks>
    /// <param name="command">The login name or address, and the features to grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request as accepted, an echo of the caller's own input.</returns>
    [HttpPost("existing")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [EnableRateLimiting(RateLimitPolicies.InvitationSending)]
    [ProducesResponseType(typeof(MembershipOfferRequestedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> OfferExistingAsync(
        [FromBody] OfferMembershipCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<MembershipOfferRequestedDto>>(command, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : ToActionResult(result);
    }

    /// <summary>Replaces the features a sub-user may use.</summary>
    /// <param name="id">The sub-user's login id.</param>
    /// <param name="command">The complete new set of features.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sub-user as they now stand.</returns>
    [HttpPut("{id:guid}/features")]
    [ProducesResponseType(typeof(SubUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeFeaturesAsync(
        Guid id,
        [FromBody] ChangeSubUserGrantsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { UserId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        return ToActionResult(await _bus.InvokeAsync<Result<SubUserDto>>(command, cancellationToken));
    }

    /// <summary>Ends a sub-user's access to the caller's account.</summary>
    /// <param name="id">The sub-user's login id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content once the access has ended.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RemoveSubUserCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
