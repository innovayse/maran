using Maran.Modules.Dns.Commands.EnableZoneSigning;
using Maran.Modules.Dns.Commands.FinishZoneSigningWithdrawal;
using Maran.Modules.Dns.Commands.WithdrawZoneSigning;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.GetZoneDnssec;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for the DNSSEC signing of one DNS zone. Thin by design: binds the request, dispatches,
/// translates.
///
/// Open to any signed-in caller, as the zone it belongs to is: the zone is looked up through the tenant
/// filter before anything else happens, so the DNSSEC state of another account's zone answers 404 whether or
/// not the zone exists. Both the zone's owner and an administrator may switch signing on. The zone identifier
/// comes from the ROUTE and is stamped onto the command here; a body cannot name a different one. No answer
/// carries the private key.
/// </summary>
[Route("api/v1/dns-zones/{zoneId:guid}/dnssec")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("DNS Zone Signing")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
[RequiresAccountFeature(AccountFeature.Dns)]
public sealed class ZoneDnssecController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ZoneDnssecController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the zone's DNSSEC situation: its step, the DS record to publish, and what the host can do.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(ZoneDnssecDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<ZoneDnssecDto>>(new GetZoneDnssecQuery(zoneId), cancellationToken));
    }

    /// <summary>Switches DNSSEC signing on: generates the key and publishes the zone signed.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("enable")]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ZoneDnssecDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnableAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        var command = new EnableZoneSigningCommand(zoneId, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<ZoneDnssecDto>>(command, cancellationToken));
    }

    /// <summary>Begins withdrawing the zone from signing; the key is kept while the DS record is removed.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("withdraw")]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ZoneDnssecDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> WithdrawAsync(Guid zoneId, CancellationToken cancellationToken)
    {
        var command = new WithdrawZoneSigningCommand(zoneId, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<ZoneDnssecDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Ends a withdrawal: the zone becomes unsigned and its key is deleted. Refused with 409 until 48 hours
    /// after the withdrawal began, for every caller, and with 400 unless the body states the DS record was removed.
    /// </summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="command">The statement that the DS record was removed at the registrar.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("finish-withdrawal")]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ZoneDnssecDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> FinishWithdrawalAsync(
        Guid zoneId,
        [FromBody] FinishZoneSigningWithdrawalCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { ZoneId = zoneId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<ZoneDnssecDto>>(command, cancellationToken));
    }
}
