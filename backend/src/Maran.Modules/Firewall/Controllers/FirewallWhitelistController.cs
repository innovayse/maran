using Maran.Modules.Firewall.Commands.AddWhitelistEntry;
using Maran.Modules.Firewall.Commands.RemoveWhitelistEntry;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Controllers.Requests;
using Maran.Modules.Firewall.Queries.ListWhitelist;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Firewall.Controllers;

/// <summary>
/// HTTP surface for the ranges the panel's automatic bans never touch. Thin by design
/// (rules/csharp.md "Controller shape is fixed").
/// </summary>
/// <remarks>
/// Administrators only, mirroring <c>AccountsController</c>'s gating: one class-level
/// <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c>. This is the surface that decides who
/// the brute-force detector may not ban, so a customer able to add a row could exempt an attacker —
/// which is why the gate is the role and not ownership.
/// </remarks>
[Route("api/v1/firewall/whitelist")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Firewall")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class FirewallWhitelistController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FirewallWhitelistController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the exempt ranges, oldest first.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WhitelistEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListWhitelistQuery();
        return ToActionResult(
            await _bus.InvokeAsync<Result<IReadOnlyList<WhitelistEntryDto>>>(query, cancellationToken));
    }

    /// <summary>Exempts a range from the automatic bans.</summary>
    /// <param name="request">The range and the note to record.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(WhitelistEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] AddWhitelistEntryRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AddWhitelistEntryCommand(request.Cidr, request.Note, ClientIpAddress, UserAgent());

        var result = await _bus.InvokeAsync<Result<WhitelistEntryDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result,
            $"/api/v1/firewall/whitelist/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Removes an exemption, so the automatic bans may reach the range again.</summary>
    /// <param name="id">The row to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// 409 when the removal would be the thing that stops exempting the address the request arrived
    /// from — <c>RemoveWhitelistEntryCommandHandler</c> says why, and the error names the way out.
    /// The caller's address is read from the request, so this endpoint's answer depends on WHERE it
    /// is called from as well as on which row is named.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new RemoveWhitelistEntryCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
