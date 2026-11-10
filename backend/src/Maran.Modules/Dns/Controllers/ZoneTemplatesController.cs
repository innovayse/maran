using Maran.Modules.Dns.Commands.DeleteZoneTemplate;
using Maran.Modules.Dns.Commands.MakeDefaultZoneTemplate;
using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Queries.GetZoneTemplate;
using Maran.Modules.Dns.Queries.GetZoneTemplateVariables;
using Maran.Modules.Dns.Queries.ListZoneTemplates;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Dns.Controllers;

/// <summary>
/// HTTP surface for the zone templates an administrator offers for new zones. Thin by design: binds,
/// dispatches, translates.
///
/// <c>AdminOnly</c>, because a template is rendered into other accounts' zones and carries the record data an
/// administrator chose. Nothing here is a customer's row, so the policy is the whole of the authorisation: a
/// customer is refused 403 on every route, and learns only a template's id, name and default flag through
/// <see cref="ZoneTemplateChoicesController"/>, never its records.
/// </summary>
[Route("api/v1/dns-zone-templates")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("DNS Zone Templates")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Dns)]
public sealed class ZoneTemplatesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ZoneTemplatesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the zone templates.</summary>
    /// <param name="query">Paging, search and sort, from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ZoneTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListZoneTemplatesQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<ZoneTemplateDto>>>(query, cancellationToken));
    }

    /// <summary>Reads the closed catalogue of variables a record's value may name, in the caller's language.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("variables")]
    [ProducesResponseType(typeof(IReadOnlyList<TemplateVariableDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVariablesAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<TemplateVariableDto>>>(
            new GetZoneTemplateVariablesQuery(), cancellationToken));
    }

    /// <summary>Reads one zone template with its records.</summary>
    /// <param name="id">The template to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ZoneTemplateDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<ZoneTemplateDetailDto>>(
            new GetZoneTemplateQuery(id), cancellationToken));
    }

    /// <summary>Creates a zone template.</summary>
    /// <param name="command">The name, the description and the records.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(ZoneTemplateDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveZoneTemplateCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { TemplateId = Guid.Empty, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<ZoneTemplateDetailDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/dns-zone-templates/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Replaces a zone template whole. A built-in template can be replaced too.</summary>
    /// <param name="id">The template to replace.</param>
    /// <param name="command">The name, the description and the records.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ZoneTemplateDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceAsync(
        Guid id,
        [FromBody] SaveZoneTemplateCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { TemplateId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<ZoneTemplateDetailDto>>(command, cancellationToken));
    }

    /// <summary>Deletes a zone template. A built-in template and the default template are refused.</summary>
    /// <param name="id">The template to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteZoneTemplateCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Makes the template the one a zone created for a new site is made from.</summary>
    /// <param name="id">The template to make the default.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/make-default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MakeDefaultAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new MakeDefaultZoneTemplateCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
