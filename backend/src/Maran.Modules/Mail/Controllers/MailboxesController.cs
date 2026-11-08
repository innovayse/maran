using Maran.Modules.Mail.Commands.ChangeMailboxPassword;
using Maran.Modules.Mail.Commands.CreateMailbox;
using Maran.Modules.Mail.Commands.DeleteMailbox;
using Maran.Modules.Mail.Commands.UpdateMailbox;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.ListMailboxes;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// HTTP surface for mailboxes. Thin by design (rules/csharp.md "Controller shape is fixed"): binds the
/// request, dispatches through Wolverine, translates the <see cref="Result{T}"/>. No business logic, no
/// data access.
///
/// Open to any signed-in caller, because a mailbox belongs to a customer and a customer manages their own.
/// What a caller can SEE is not decided here: every read and every mutation goes through
/// <c>MailDbContext</c>, whose global query filter scopes mailbox rows to the caller's account, so a
/// mailbox belonging to somebody else answers 404 — never 403, which would confirm it exists (spec §8,
/// rules/testing.md item 3). No route takes an address, only a row id.
///
/// A password is accepted by the create and change-password actions and returned by none. Create, change
/// password and delete are refused while an operator is viewing as the customer: each makes a credential,
/// or destroys mail, and a look must leave neither behind.
/// </summary>
[Route("api/v1/mailboxes")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Mailboxes")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Mail)]
public sealed class MailboxesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public MailboxesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the mailboxes the caller may see.</summary>
    /// <param name="query">
    /// Paging, search and sort, and the <c>accountId</c>, <c>domain</c> and <c>status</c> filters, from the
    /// query string; the filters are ANDed with the caller's tenant scope.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<MailboxDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListMailboxesQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<MailboxDto>>>(query, cancellationToken));
    }

    /// <summary>Creates a mailbox in one of the caller's mail domains.</summary>
    /// <param name="command">The domain, the local part, the password the owner chose, and the quota.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailboxDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateMailboxCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<MailboxDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/mailboxes/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Changes a mailbox's size limit, hourly send limit and on/off state. Another customer's mailbox
    /// answers 404, not 403.
    /// </summary>
    /// <param name="id">The mailbox to change.</param>
    /// <param name="command">The new limits and state.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailboxDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateMailboxCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { MailboxId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MailboxDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Gives the mailbox a new password. Nothing that is already open is ended. Another customer's
    /// mailbox answers 404, not 403.
    /// </summary>
    /// <param name="id">The mailbox to re-credential.</param>
    /// <param name="command">The new password.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}/password")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePasswordAsync(
        Guid id,
        [FromBody] ChangeMailboxPasswordCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { MailboxId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>
    /// Removes the mailbox and the mail it holds. Another customer's mailbox answers 404, not 403.
    /// </summary>
    /// <param name="id">The mailbox to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteMailboxCommand(id, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
