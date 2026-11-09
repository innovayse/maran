using Maran.Modules.Dns.Commands.SaveDnsSettings;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.GetDnsSettings;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for the settings every zone is published with: the name servers, the SOA mailbox, the
/// addresses and the default time to live. Thin by design: binds, dispatches, translates.
///
/// <c>AdminOnly</c>, because the settings are rendered into every account's zones. Nothing here is a
/// customer's row, so the policy is the whole of the authorisation.
/// </summary>
[Route("api/v1/dns-settings")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("DNS Settings")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
public sealed class DnsSettingsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DnsSettingsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the settings every zone is published with.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DnsSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(
            await _bus.InvokeAsync<Result<DnsSettingsDto>>(new GetDnsSettingsQuery(), cancellationToken));
    }

    /// <summary>Saves the settings every zone is published with.</summary>
    /// <param name="command">The name servers, SOA mailbox, addresses and default time to live.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ProducesResponseType(typeof(DnsSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveDnsSettingsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<DnsSettingsDto>>(command, cancellationToken));
    }
}
