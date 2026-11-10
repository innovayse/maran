using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.ListZoneTemplateChoices;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// The zone templates a caller who may create a zone can pick from, for the creation form: an id, a name and the
/// default flag, never a template's records. Open to any signed-in caller whose plan sells DNS, with the policy and
/// the feature gate of <see cref="ZonesController"/>, because it is part of creating a zone. It is a controller of
/// its own, as <see cref="DnsRecordTypesController"/> is, so the administrator's template controller stays
/// administrator-only in its entirety.
/// </summary>
[Route("api/v1/dns-zones/templates/choices")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("DNS Zones")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
[RequiresAccountFeature(AccountFeature.Dns)]
public sealed class ZoneTemplateChoicesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ZoneTemplateChoicesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the templates a new zone can be made from.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ZoneTemplateChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<ZoneTemplateChoiceDto>>>(
            new ListZoneTemplateChoicesQuery(), cancellationToken));
    }
}
