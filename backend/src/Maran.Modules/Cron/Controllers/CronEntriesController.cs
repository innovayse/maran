using Maran.Modules.Cron.Commands.CreateCronEntry;
using Maran.Modules.Cron.Commands.DeleteCronEntry;
using Maran.Modules.Cron.Commands.SetCronEntryEnabled;
using Maran.Modules.Cron.Commands.UpdateCronEntry;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Controllers.Requests;
using Maran.Modules.Cron.Queries.GetCronEntryOutput;
using Maran.Modules.Cron.Queries.ListCronEntries;
using Maran.Modules.Cron.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Cron.Controllers;

/// <summary>
/// HTTP surface for an account's scheduled tasks. Thin by design (rules/csharp.md "Controller shape
/// is fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>. No business logic, no data access.
///
/// Open to any signed-in caller, because a cron entry belongs to a customer and a customer manages
/// their own. What they can SEE is not decided here: every route names an account by row id, and the
/// handler resolves it through <c>IAccountDirectory</c>, which answers null for an account the caller
/// does not own — so another customer's account, and every entry under it, answers 404 and never
/// 403, which would confirm it exists (spec §8, rules/testing.md item 3).
/// </summary>
/// <remarks>
/// <b>Every route names the account explicitly, which no other module's controller has to do.</b>
/// The others carry a row id whose ownership a tenant query filter decides; this module keeps no
/// rows, so the account cannot be inferred from the entry — an entry id means nothing until it is
/// asked of one account's crontab. The account therefore travels as a query parameter on the reads
/// and in the body on the writes, and the resolution in the handler is the whole tenant boundary.
///
/// The customer's command travels through this controller in both directions and is written to no
/// log line on the way, at any level (RULING 31, <see cref="CronAuditJournal"/>).
/// </remarks>
[Route("api/v1/cron-entries")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Cron")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class CronEntriesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public CronEntriesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one account's cron entries. Another customer's account answers 404, not 403.</summary>
    /// <param name="accountId">The account whose crontab to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CronEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var query = new ListCronEntriesQuery(accountId);
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<CronEntryDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Installs a new cron entry and returns it, including the identifier the agent minted for it.
    /// </summary>
    /// <param name="request">The owning account, the schedule and the command.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CronEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateCronEntryCommand(
            request.AccountId, request.Schedule, request.Command, ClientIpAddress, UserAgent());

        var result = await _bus.InvokeAsync<Result<CronEntryDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result,
            $"/api/v1/cron-entries/{(result.IsSuccess ? result.Value.EntryId : string.Empty)}"
            + $"/output?accountId={request.AccountId}");
    }

    /// <summary>
    /// Replaces an entry's schedule and command, leaving its enablement exactly as it was. Another
    /// customer's entry answers 404, not 403.
    /// </summary>
    /// <param name="entryId">The entry to rewrite.</param>
    /// <param name="request">The owning account, the new schedule and the new command.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{entryId}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        string entryId,
        [FromBody] UpdateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCronEntryCommand(
            request.AccountId, entryId, request.Schedule, request.Command, ClientIpAddress, UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>
    /// Switches an entry on or off without touching what it runs. Another customer's entry answers
    /// 404, not 403.
    /// </summary>
    /// <param name="entryId">The entry to switch.</param>
    /// <param name="request">The owning account and the state to put the entry in.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{entryId}/enabled")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnabledAsync(
        string entryId,
        [FromBody] SetCronEntryEnabledRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SetCronEntryEnabledCommand(
            request.AccountId, entryId, request.Enabled, ClientIpAddress, UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>
    /// Reads what the entry's last run left behind, or nothing at all when it has never run. Another
    /// customer's entry answers 404, not 403.
    /// </summary>
    /// <param name="entryId">The entry to read.</param>
    /// <param name="accountId">The account whose crontab holds it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{entryId}/output")]
    [ProducesResponseType(typeof(CronEntryOutputDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOutputAsync(
        string entryId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var query = new GetCronEntryOutputQuery(accountId, entryId);
        return ToActionResult(await _bus.InvokeAsync<Result<CronEntryOutputDto?>>(query, cancellationToken));
    }

    /// <summary>
    /// Removes the entry, together with the files that held its command and its last run. Another
    /// customer's entry answers 404, not 403.
    /// </summary>
    /// <param name="entryId">The entry to remove.</param>
    /// <param name="accountId">The account whose crontab holds it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{entryId}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(
        string entryId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteCronEntryCommand(accountId, entryId, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
