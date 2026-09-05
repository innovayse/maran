using Maran.Modules.Firewall.Commands.AllowPort;
using Maran.Modules.Firewall.Commands.DenyPort;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Controllers.Requests;
using Maran.Modules.Firewall.Queries.ListRules;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Firewall.Controllers;

/// <summary>
/// HTTP surface for the host firewall's port rules. Thin by design (rules/csharp.md "Controller
/// shape is fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>. No business logic, no data access.
/// </summary>
/// <remarks>
/// <para>
/// Administrators only, mirroring <c>AccountsController</c>'s gating: one class-level
/// <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c>, whose policy the Host defines in
/// <c>RolePolicies</c>. A signed-in customer is answered 403 and an anonymous caller 401 — the same
/// answers the accounts surface gives, and deliberately not the 404 a tenant-scoped resource gives:
/// there is no tenant here to hide behind. A firewall rule is a fact about the whole machine, its
/// existence discloses nothing about any customer, and the honest answer to a customer asking is
/// that this is not theirs to see.
/// </para>
/// <para>
/// The two host facts every one of these calls carries — the SSH ports and the panel's public port —
/// are nowhere in this file, and no route accepts them. They come from <c>FirewallOptions</c> in the
/// handlers.
/// </para>
/// </remarks>
[Route("api/v1/firewall/rules")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Firewall")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class FirewallRulesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FirewallRulesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the port rules the firewall is running.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FirewallRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListRulesQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<FirewallRuleDto>>>(query, cancellationToken));
    }

    /// <summary>Opens a port, optionally scoped to one source range.</summary>
    /// <param name="request">The port, protocol and source range to allow.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] AllowPortRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AllowPortCommand(
            request.Port, request.Protocol, request.SourceCidr, ClientIpAddress, UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Closes a port that was opened, matching the source range the allow was scoped to.</summary>
    /// <param name="request">The port, protocol and source range to stop allowing.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteAsync(
        [FromQuery] DenyPortRequest request,
        CancellationToken cancellationToken)
    {
        var command = new DenyPortCommand(
            request.Port, request.Protocol, request.SourceCidr, ClientIpAddress, UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
