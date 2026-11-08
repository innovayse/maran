using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Commands.DeleteZone;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.GetZone;
using Maran.Modules.Dns.Queries.ListZones;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for DNS zones. Thin by design (rules/csharp.md "Controller shape is fixed"): binds the
/// request, dispatches through Wolverine, translates the <see cref="Result{T}"/>. No business logic, no data
/// access.
///
/// Open to any signed-in caller, because a zone belongs to an account and its owner manages it. What a caller
/// can SEE is not decided here: every read and every mutation goes through <c>DnsDbContext</c>, whose global
/// query filter scopes zone rows to the caller's account and shows a reseller, an API key and an anonymous
/// caller none, so a zone belonging to somebody else answers 404, never 403, which would confirm it exists
/// (spec §8, rules/testing.md item 3). No route on this controller takes a zone NAME, only a row id.
/// </summary>
[Route("api/v1/dns-zones")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("DNS Zones")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
[RequiresAccountFeature(AccountFeature.Dns)]
public sealed class ZonesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ZonesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the zones the caller may see.</summary>
    /// <param name="query">
    /// Paging, search and sort, and the <c>accountId</c> filter, from the query string; the filter is ANDed
    /// with the caller's tenant scope.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<ZoneDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListZonesQuery query, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<ZoneDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one zone. Another account's zone answers 404, not 403.</summary>
    /// <param name="id">The zone to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(ZoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ZoneDto>>(new GetZoneQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Creates an empty zone for an account and hands it to the name server.</summary>
    /// <param name="command">The owning account and the apex name.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ZoneDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateZoneCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<ZoneDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/dns-zones/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Removes the zone and every record in it. Another account's zone answers 404, not 403.
    /// </summary>
    /// <param name="id">The zone to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteZoneCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
