using Maran.Modules.Dns.Commands.DisableDns;
using Maran.Modules.Dns.Commands.EnableDns;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.GetDnsStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for the server's own name server: switching it on and off, reading what it is doing, and
/// the settings every zone is published with. Thin by design: binds, dispatches, translates.
///
/// <c>AdminOnly</c>, because it starts a daemon that listens on every address and because the settings are rendered into
/// every account's zones. Nothing here is a customer's row, so there is no tenant filter to lean on:
/// the policy is the whole of the authorisation, and a customer, a reseller and an API key are all refused
/// before a handler runs.
/// </summary>
[Route("api/v1/dns-server")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("DNS Server")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
public sealed class DnsServerController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DnsServerController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads what the name server is actually doing, next to what was asked of it.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DnsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<DnsStatusDto>>(new GetDnsStatusQuery(), cancellationToken));
    }

    /// <summary>Switches the name server on. The firewall is not touched; port 53 is opened separately.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("enable")]
    [ProducesResponseType(typeof(DnsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnableAsync(CancellationToken cancellationToken)
    {
        var command = new EnableDnsCommand(ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<DnsStatusDto>>(command, cancellationToken));
    }

    /// <summary>Switches the name server off. The firewall is not touched. Zones and their files stay.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("disable")]
    [ProducesResponseType(typeof(DnsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DisableAsync(CancellationToken cancellationToken)
    {
        var command = new DisableDnsCommand(ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<DnsStatusDto>>(command, cancellationToken));
    }
}
