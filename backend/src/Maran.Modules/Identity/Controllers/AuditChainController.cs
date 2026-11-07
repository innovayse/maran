using Maran.Modules.Identity.Commands.VerifyAuditChain;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.GetAuditChainStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// HTTP surface for the audit journal's tamper-evidence chain: how far it is sealed, and a walk that
/// checks it. Thin by design: binds the request, dispatches through Wolverine, translates the result.
/// </summary>
/// <remarks>
/// Administrators only, and refused while viewing as a customer. The verification reads every sealed
/// entry, so it is the one action here that costs the database real work; it is behind the general API
/// rate limit, and the verifier lets only one walk run at a time on the panel.
/// </remarks>
[Route("api/v1/audit/chain")]
[Tags("Audit Chain")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class AuditChainController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The administrator making the request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AuditChainController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads how far the journal has been sealed into the chain.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The sealed and unsealed counts, the newest sealed position and the purge point.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(AuditChainStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<AuditChainStatusDto>>(
            new GetAuditChainStatusQuery(), cancellationToken));
    }

    /// <summary>Walks the sealed journal and reports whether the chain still holds.</summary>
    /// <remarks>
    /// A broken chain is a successful answer with <c>intact: false</c>, not an error: the request did what
    /// it was asked and the finding is the content. The call is journalled either way.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The verdict.</returns>
    [HttpPost("verification")]
    [ProducesResponseType(typeof(AuditChainVerificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyAsync(CancellationToken cancellationToken)
    {
        var command = new VerifyAuditChainCommand(ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<AuditChainVerificationDto>>(command, cancellationToken));
    }
}
