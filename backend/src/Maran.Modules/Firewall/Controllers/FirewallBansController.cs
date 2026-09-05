using Maran.Modules.Firewall.Commands.BanAddress;
using Maran.Modules.Firewall.Commands.UnbanAddress;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Controllers.Requests;
using Maran.Modules.Firewall.Queries.ListBans;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Firewall.Controllers;

/// <summary>
/// HTTP surface for the host's address bans. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only, mirroring <c>AccountsController</c>'s gating: one class-level
/// <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c>. A signed-in customer is answered
/// 403 and an anonymous caller 401. Who is banned from the server is not a tenant's business, and
/// there is no tenant dimension to scope it by.
/// </remarks>
[Route("api/v1/firewall/bans")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Firewall")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class FirewallBansController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FirewallBansController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the bans still in force, newest first, each with the reason it was placed.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListBansQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<BanDto>>>(query, cancellationToken));
    }

    /// <summary>Bans an address, for a duration or until somebody lifts it.</summary>
    /// <param name="request">The address to ban and how long for.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] BanAddressRequest request,
        CancellationToken cancellationToken)
    {
        var command = new BanAddressCommand(
            request.Address, request.DurationMinutes, ClientIpAddress, UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Lifts every ban in force for one address.</summary>
    /// <param name="address">The address to let back in.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// The address travels in the query string rather than in the path: an IPv6 address is full of
    /// colons, and a route segment carrying them is at the mercy of every proxy and client library
    /// between the browser and here.
    /// </remarks>
    [HttpDelete]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(
        [FromQuery] string address,
        CancellationToken cancellationToken)
    {
        var command = new UnbanAddressCommand(address ?? string.Empty, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
