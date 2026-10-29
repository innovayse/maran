using Maran.Modules.Identity.Commands.IssueInvitationLink;
using Maran.Modules.Identity.Commands.ResendInvitation;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.GetInvitationStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Administrator surface for outstanding invitations: reading where one stands, resending it when
/// the first mail never arrived or its token has expired, and creating a link to hand over by hand
/// on an installation that cannot send mail. Thin by design (rules/csharp.md "Controller shape is fixed"):
/// binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <b>Every action here is administrator-only, and that is the whole of why returning an
/// invitation link is safe.</b> The class-level policy is evaluated before any handler runs, so a
/// customer is refused with 403 and an anonymous caller with 401 without the answer depending on
/// which accounts exist. A new action added to this controller inherits that policy; one that
/// needed to be laxer would belong on another controller.
/// </remarks>
[Route("api/v1/invitations")]
[Tags("Invitations")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class InvitationsController : BaseApiController
{
    /// <summary>The message bus commands are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus commands are dispatched through.</param>
    public InvitationsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Retires every outstanding invitation token for the account's login and issues a new one.
    /// </summary>
    /// <remarks>
    /// This action has no body: the account id from the route is everything the command needs
    /// (rules/csharp.md "An action with no body constructs rather than binds"), so the command is
    /// built directly here rather than bound from a request.
    /// </remarks>
    /// <param name="accountId">The hosting account whose owner is being (re)invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{accountId:guid}/resend")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var command = new ResendInvitationCommand(accountId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>
    /// Reports where an account owner's invitation stands: none issued, outstanding, expired or
    /// accepted. Carries no token and no link.
    /// </summary>
    /// <param name="accountId">The hosting account being asked about.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType(typeof(InvitationStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatusAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var query = new GetInvitationStatusQuery(accountId);
        return ToActionResult(await _bus.InvokeAsync<Result<InvitationStatusDto>>(query, cancellationToken));
    }

    /// <summary>
    /// Issues a fresh invitation and returns its one-time link to the administrator, retiring every
    /// outstanding token for the account's login. Nothing is mailed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The answer is a credential, so it is marked uncacheable.</b> <c>no-store</c> tells the
    /// browser and any intermediary not to keep the body; the route is a POST, which shared caches do
    /// not store in any case, and the host has no response-caching or idempotency layer that would
    /// persist it. This action has no body: the account id from the route is everything the command
    /// needs (rules/csharp.md "An action with no body constructs rather than binds").
    /// </para>
    /// </remarks>
    /// <param name="accountId">The hosting account whose owner is being invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{accountId:guid}/link")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(InvitationLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueLinkAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var command = new IssueInvitationLinkCommand(accountId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<InvitationLinkDto>>(command, cancellationToken));
    }
}
