using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Commands.ChangeSubUserGrants;
using Maran.Modules.Identity.Commands.InviteSubUser;
using Maran.Modules.Identity.Commands.RemoveSubUser;
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
/// The people an account's owner added to the account: invite one, choose which features they may use,
/// and end their access.
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
[RefusedWhileViewingAs]
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
    /// Adds a person to the caller's account and mails them an invitation. Rate limited by the
    /// invitation policy and not the general one: this is the one action that makes the panel send mail
    /// to an address the caller typed, so it must not be a way to send a stranger a stream of mail.
    /// </summary>
    /// <param name="command">Their login name, address and features.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new sub-user.</returns>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Invitation)]
    [ProducesResponseType(typeof(SubUserDto), StatusCodes.Status201Created)]
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

        var result = await _bus.InvokeAsync<Result<SubUserDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/me/sub-users/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
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
    /// <returns><c>true</c> when the access ended.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RemoveSubUserCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }
}
