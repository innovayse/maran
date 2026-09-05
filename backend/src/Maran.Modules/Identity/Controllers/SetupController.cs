using Maran.Modules.Identity.Commands.CompleteSetup;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Controllers.Requests;
using Maran.Modules.Identity.Queries.GetSetupState;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// First-run setup: the one path by which a panel with no users gets its administrator. Anonymous
/// by necessity — there is nobody to authenticate yet — and closed permanently the moment it
/// succeeds, because the command refuses once any user exists.
/// </summary>
[Route("api/v1/setup")]
[Tags("Setup")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class SetupController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal, anonymous on this controller.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public SetupController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reports whether the panel already has an administrator.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("state")]
    [ProducesResponseType(typeof(SetupStateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStateAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<SetupStateDto>>(new GetSetupStateQuery(), cancellationToken));
    }

    /// <summary>Creates the first administrator.</summary>
    /// <remarks>
    /// Rate limited with the login policy: the token is the only thing standing between a stranger
    /// and ownership of the server, so guessing at it must be as expensive as guessing a password.
    /// </remarks>
    /// <param name="request">The token and the administrator's details.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(AuthenticatedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CompleteAsync(
        [FromBody] CompleteSetupRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CompleteSetupCommand(
            request.Token,
            request.Username,
            request.Email,
            request.Password,
            ClientIpAddress,
            CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<AuthenticatedUserDto>>(command, cancellationToken));
    }

}
