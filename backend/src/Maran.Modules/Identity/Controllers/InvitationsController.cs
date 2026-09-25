using Maran.Modules.Identity.Commands.ResendInvitation;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// Administrator surface for outstanding invitations: resending one when the first mail never
/// arrived or its token has expired. Thin by design (rules/csharp.md "Controller shape is fixed"):
/// binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
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
}
