using Maran.Modules.Mail.Commands.ClearMailboxAutoresponder;
using Maran.Modules.Mail.Commands.SetMailboxAutoresponder;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.GetMailboxAutoresponder;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// HTTP surface for the automatic reply of one mailbox. Thin by design (rules/csharp.md "Controller shape
/// is fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
///
/// The reply is a sub-resource of the mailbox, addressed by the mailbox's row id. A mailbox belonging to
/// somebody else answers 404 on every action — never 403 — because the context's query filter hides the
/// mailbox and its reply alike (spec §8, rules/testing.md item 3). Setting and clearing the reply are
/// refused while an operator is viewing as the customer: a reply keeps answering other people's mail after
/// the look has ended.
/// </summary>
[Route("api/v1/mailboxes/{mailboxId:guid}/autoresponder")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Mailbox Autoresponders")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Mail)]
public sealed class MailboxAutoresponderController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public MailboxAutoresponderController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the mailbox's automatic reply. 404 when the mailbox has none, or is not the caller's.</summary>
    /// <param name="mailboxId">The mailbox.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(MailAutoresponderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid mailboxId, CancellationToken cancellationToken)
    {
        var query = new GetMailboxAutoresponderQuery(mailboxId);

        return ToActionResult(await _bus.InvokeAsync<Result<MailAutoresponderDto>>(query, cancellationToken));
    }

    /// <summary>Sets the mailbox's automatic reply, replacing the one it had.</summary>
    /// <param name="mailboxId">The mailbox.</param>
    /// <param name="command">The subject, body, dates and reply interval.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(MailAutoresponderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetAsync(
        Guid mailboxId,
        [FromBody] SetMailboxAutoresponderCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { MailboxId = mailboxId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MailAutoresponderDto>>(command, cancellationToken));
    }

    /// <summary>Clears the mailbox's automatic reply. Clearing a mailbox that has none succeeds.</summary>
    /// <param name="mailboxId">The mailbox.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClearAsync(Guid mailboxId, CancellationToken cancellationToken)
    {
        var command = new ClearMailboxAutoresponderCommand(mailboxId, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
