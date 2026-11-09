using Maran.Modules.Mail.Commands.DeleteQueuedMessage;
using Maran.Modules.Mail.Commands.FlushMailQueue;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.ListMailQueue;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// HTTP surface for the host's mail queue: read it, remove one message, retry the deferred ones.
/// Thin by design (rules/csharp.md "Controller shape is fixed").
/// </summary>
/// <remarks>
/// <b><c>AdminOnly</c> on the class.</b> The queue holds every account's mail in flight, senders and
/// recipient counts included, so it is an operator's view and no customer token reaches it. Nothing
/// here is tenant-scoped; the policy IS the authorisation.
/// </remarks>
[Route("api/v1/mail-queue")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Mail Queue")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MailQueueController : BaseApiController
{
    /// <summary>The message bus every action dispatches through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The caller, for the base class.</param>
    /// <param name="bus">The message bus.</param>
    public MailQueueController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the queued messages, bounded by the agent.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries, the total and whether the list is truncated.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(MailQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListMailQueueQuery();

        return ToActionResult(await _bus.InvokeAsync<Result<MailQueueDto>>(query, cancellationToken));
    }

    /// <summary>Removes one queued message.</summary>
    /// <param name="id">The mail transfer agent's queue id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var command = new DeleteQueuedMessageCommand(id, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>Asks the mail transfer agent to retry every deferred message now.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The queue size after the request.</returns>
    [HttpPost("flush")]
    [ProducesResponseType(typeof(MailQueueFlushDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FlushAsync(CancellationToken cancellationToken)
    {
        var command = new FlushMailQueueCommand(ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<MailQueueFlushDto>>(command, cancellationToken));
    }
}
