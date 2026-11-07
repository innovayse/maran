using Maran.Modules.Identity.Commands.SaveAuditRetention;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.GetAuditRetention;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// HTTP surface for how long the audit journal is kept. Thin by design: binds the request, dispatches
/// through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only. Changing the window decides when the panel's security record starts being
/// deleted, so the write is the most sensitive action on this page, and the read is restricted with
/// it so the screen is one thing rather than two with different rules. Every change is journalled by
/// the handler with the old and the new value.
/// </remarks>
[Route("api/v1/audit/retention")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Audit Retention")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class AuditRetentionController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AuditRetentionController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads how long the audit journal is kept.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AuditRetentionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetAuditRetentionQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<AuditRetentionDto>>(query, cancellationToken));
    }

    /// <summary>Replaces how long the audit journal is kept.</summary>
    /// <remarks>
    /// A PUT rather than a POST: there is exactly one setting on a panel, the request carries all of
    /// it, and repeating the same body twice leaves the panel in the same state.
    /// </remarks>
    /// <param name="command">The window to save. The caller's address and user agent are stamped
    /// here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveAuditRetentionCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
