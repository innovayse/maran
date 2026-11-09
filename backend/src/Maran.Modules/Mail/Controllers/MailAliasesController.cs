using Maran.Modules.Mail.Commands.CreateMailAlias;
using Maran.Modules.Mail.Commands.DeleteMailAlias;
using Maran.Modules.Mail.Commands.UpdateMailAlias;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.ListMailAliases;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// The caller's aliases, forwarders and catch-alls: list, create, change where one delivers, delete.
/// </summary>
/// <remarks>
/// <para>
/// Account-scoped by the context's query filter, never by an argument: a customer sees and changes their
/// own addresses only, and another account's identifier answers 404. The account feature is checked on
/// every request, so a plan without mail is refused whatever the menu shows.
/// </para>
/// <para>
/// An operator viewing the panel as a customer may read the list and nothing else: an address is a route
/// for mail, and a forwarder is a way to copy an account's mail somewhere outside it.
/// </para>
/// </remarks>
[Route("api/v1/mail-aliases")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Mail Aliases")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Mail)]
public sealed class MailAliasesController : BaseApiController
{
    /// <summary>The bus every operation is dispatched on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The bus every operation is dispatched on.</param>
    public MailAliasesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the addresses the caller may see, with search, sort and filters.</summary>
    /// <param name="query">Paging, search, sort and the account, domain and kind filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of addresses and the total that matched.</returns>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<MailAliasDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListMailAliasesQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<MailAliasDto>>>(query, cancellationToken));
    }

    /// <summary>Creates an alias, a forwarder or the catch-all of a domain.</summary>
    /// <param name="command">The domain, kind, local part and targets.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new address.</returns>
    [HttpPost]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailAliasDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateMailAliasCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<MailAliasDto>>(command, cancellationToken);

        return ToCreatedActionResult(
            result, $"/api/v1/mail-aliases/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Replaces where an address delivers.</summary>
    /// <param name="id">The address.</param>
    /// <param name="command">The new targets.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The address as it now reads.</returns>
    [HttpPut("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailAliasDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateMailAliasCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { MailAliasId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MailAliasDto>>(command, cancellationToken));
    }

    /// <summary>Deletes an address.</summary>
    /// <param name="id">The address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteMailAliasCommand(id, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
