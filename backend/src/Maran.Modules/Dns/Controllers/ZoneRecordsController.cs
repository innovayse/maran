using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.ChangeRecord;
using Maran.Modules.Dns.Commands.DeleteRecord;
using Maran.Modules.Dns.Commands.ReplaceRecords;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.ListRecords;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for the records of one DNS zone. Thin by design: binds the request, dispatches, translates.
///
/// Every route is under a zone, and the zone is looked up through the tenant filter before anything else
/// happens, so a record of another account's zone answers 404 whether the caller guessed the zone id, the
/// record id or both. The zone and record identifiers come from the ROUTE and are stamped onto the command
/// here; the body cannot name a different one.
/// </summary>
[Route("api/v1/dns-zones/{zoneId:guid}/records")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("DNS Records")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
[RequiresAccountFeature(AccountFeature.Dns)]
public sealed class ZoneRecordsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ZoneRecordsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of a zone's records.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="query">Paging, search, sort and the type filter, from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<ZoneRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllAsync(
        Guid zoneId,
        [FromQuery] ListRecordsQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { ZoneId = zoneId };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<ZoneRecordDto>>>(query, cancellationToken));
    }

    /// <summary>Adds a record to the zone.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="command">The record as stated.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ZoneRecordDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAsync(
        Guid zoneId,
        [FromBody] AddRecordCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { ZoneId = zoneId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<ZoneRecordDto>>(command, cancellationToken);
        return ToCreatedActionResult(result, $"/api/v1/dns-zones/{zoneId}/records");
    }

    /// <summary>Replaces what one record says.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="recordId">The record.</param>
    /// <param name="command">The record as it should now read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{recordId:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ZoneRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeAsync(
        Guid zoneId,
        Guid recordId,
        [FromBody] ChangeRecordCommand command,
        CancellationToken cancellationToken)
    {
        command = command with
        {
            ZoneId = zoneId,
            RecordId = recordId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<ZoneRecordDto>>(command, cancellationToken));
    }

    /// <summary>Replaces the customer's whole record set in one step, as an import does.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="command">The complete new record set.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [Idempotent]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceAsync(
        Guid zoneId,
        [FromBody] ReplaceRecordsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { ZoneId = zoneId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Removes one record from the zone.</summary>
    /// <param name="zoneId">The zone.</param>
    /// <param name="recordId">The record.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{recordId:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid zoneId, Guid recordId, CancellationToken cancellationToken)
    {
        var command = new DeleteRecordCommand(zoneId, recordId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
