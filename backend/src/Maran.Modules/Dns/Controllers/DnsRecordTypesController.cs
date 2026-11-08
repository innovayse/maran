using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.ListRecordTypes;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for the reference data a record form is drawn from. Thin by design: dispatches and
/// translates. The answer is the same for every caller and names no account's row.
/// </summary>
[Route("api/v1/dns-zones/record-types")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("DNS Record Types")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
[RequiresAccountFeature(AccountFeature.Dns)]
public sealed class DnsRecordTypesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DnsRecordTypesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the record types, which fields each uses, and the bounds, in the caller's language.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(RecordTypeCatalogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<RecordTypeCatalogDto>>(new ListRecordTypesQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
