using Maran.Modules.Identity.Commands.SaveSecurityPolicy;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Controllers.Requests;
using Maran.Modules.Identity.Queries.GetSecurityPolicy;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// HTTP surface for the panel's security policy (R12). Thin by design: binds the request, dispatches
/// through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only, and not because the values are secret — they are printed on the login screen
/// as "at least twelve characters" the moment somebody gets one wrong. They are administrator-only
/// because CHANGING them changes every account on the panel at once, and the read is restricted with
/// the write so the screen is one thing rather than two with different rules.
/// </remarks>
[Route("api/v1/security-policy")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Security Policy")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SecurityPolicyController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public SecurityPolicyController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the policy the panel is enforcing.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(SecurityPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetSecurityPolicyQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<SecurityPolicyDto>>(query, cancellationToken));
    }

    /// <summary>Replaces the panel's security policy.</summary>
    /// <remarks>
    /// A PUT rather than a POST: there is exactly one policy on a panel, the request carries all of
    /// it, and repeating the same body twice leaves the panel in the same state.
    /// </remarks>
    /// <param name="request">The policy to save.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveSecurityPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SaveSecurityPolicyCommand(
            request.MinimumPasswordLength,
            request.ForceTwoFactorForAdmins,
            request.MaxFailedLoginAttempts,
            request.LockoutMinutes,
            ClientIpAddress,
            CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

}
