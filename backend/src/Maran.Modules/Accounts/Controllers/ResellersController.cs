using Maran.Modules.Accounts.Commands.ChangeResellerEnvelope;
using Maran.Modules.Accounts.Commands.CreateReseller;
using Maran.Modules.Accounts.Commands.DeleteReseller;
using Maran.Modules.Accounts.Commands.ReactivateReseller;
using Maran.Modules.Accounts.Commands.SuspendReseller;
using Maran.Modules.Accounts.Commands.UpdateReseller;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetReseller;
using Maran.Modules.Accounts.Queries.ListResellerChoices;
using Maran.Modules.Accounts.Queries.ListResellers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for managing the resellers a server sells through. Thin by design (rules/csharp.md
/// "Controller shape is fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>. No business logic, no data access.
///
/// Administrators only. What a reseller is sold, and whether it may act at all, is the server owner's
/// decision; a reseller reads its own standing through <c>GET /api/v1/reseller/me</c>
/// (<see cref="ResellerController"/>), never through here, and an administrator who is viewing the panel
/// as a reseller holds a reseller token that this policy refuses.
/// </summary>
[Route("api/v1/resellers")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Resellers")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ResellersController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public ResellersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of resellers, searched, filtered by state and sorted, each with what it owns.</summary>
    /// <param name="query">Paging, search, sort and the state filter, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ResellerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListResellersQuery query, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<ResellerDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Lists one page of reseller ids and names for a picker, so the account form's owner select never
    /// has to load the full reseller list.
    /// </summary>
    /// <param name="query">Paging, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("choices")]
    [ProducesResponseType(typeof(PagedResult<ResellerChoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetChoicesAsync(
        [FromQuery] ListResellerChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<ResellerChoiceDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Reads one reseller with its envelope and what it owns.</summary>
    /// <param name="id">The reseller to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(new GetResellerQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates a reseller and, through an event, the login it signs in with; the login's invitation is
    /// mailed to the contact address in the contact's language.
    /// </summary>
    /// <param name="command">
    /// The reseller's name, contact address, language and envelope. The command is bound straight from
    /// the body — there is no request type in between — and the two audit fields it also carries are
    /// unbindable by construction (see <see cref="CreateResellerCommand"/>), so the stamp below is the
    /// only thing that can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateResellerCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(stamped, cancellationToken);
        return ToCreatedActionResult(
            result,
            $"/api/v1/resellers/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Renames a reseller and replaces its contact address and language. The reseller's login follows
    /// the name and the address, so the new name is what it signs in with.
    /// </summary>
    /// <param name="id">The reseller to change.</param>
    /// <param name="command">The new name, contact address and language. Its reseller id, address and agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateResellerCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { ResellerId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Replaces what a reseller was sold. Lowering it below what the reseller already owns is accepted:
    /// its existing plans and accounts keep working and only new writes are refused.
    /// </summary>
    /// <param name="id">The reseller whose envelope changes.</param>
    /// <param name="command">The new envelope. Its reseller id, address and agent are stamped here.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}/envelope")]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeEnvelopeAsync(
        Guid id,
        [FromBody] ChangeResellerEnvelopeCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { ResellerId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(stamped, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Suspends a reseller: its login is refused and its mutations are refused. Its customers' accounts
    /// are not suspended with it. Idempotent.
    /// </summary>
    /// <param name="id">The reseller to suspend.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new SuspendResellerCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Lifts a reseller's suspension. Idempotent, for the same reason suspension is.</summary>
    /// <param name="id">The reseller to reactivate.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(ResellerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ReactivateResellerCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<ResellerDetailDto>>(command, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deletes a reseller and its login. Refused with 409 while the reseller still owns accounts or
    /// plans.
    /// </summary>
    /// <param name="id">The reseller to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteResellerCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result>(command, cancellationToken);
        return ToActionResult(result);
    }
}
