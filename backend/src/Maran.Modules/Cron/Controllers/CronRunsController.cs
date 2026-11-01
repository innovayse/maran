using Maran.Modules.Cron.Commands.RunCronEntry;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Queries.GetCronRun;
using Maran.Modules.Cron.Queries.ListCronRuns;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Cron.Controllers;

/// <summary>
/// HTTP surface for running a cron entry by hand and reading the runs that produced (issue #158). Thin
/// by design (rules/csharp.md "Controller shape is fixed").
/// </summary>
/// <remarks>
/// Open to any signed-in caller, like the entries themselves: the account is named explicitly and the
/// handler's resolution through <c>IAccountDirectory</c> is the tenant boundary, so another customer's
/// account answers 404, never 403. What a run printed travels back to its owner and is written to no
/// log line on the way.
/// </remarks>
[Route("api/v1/cron-entries/{entryId}/runs")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Cron")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class CronRunsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public CronRunsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Runs the entry now, as its account, and answers with the recorded run once it has ended. The
    /// job's own failure is a run, not an error. Another customer's entry answers 404.
    /// </summary>
    /// <param name="entryId">The entry to run.</param>
    /// <param name="command">The owning account.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CronRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RunAsync(
        [FromRoute] string entryId,
        [FromBody] RunCronEntryCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { EntryId = entryId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<CronRunDto>>(command, cancellationToken));
    }

    /// <summary>One server page of the entry's runs, newest first. Another customer's account answers 404.</summary>
    /// <param name="entryId">The entry whose runs are listed.</param>
    /// <param name="query">The account, paging, search, sort and outcome filter.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CronRunSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllAsync(
        [FromRoute] string entryId,
        [FromQuery] ListCronRunsQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { EntryId = entryId };

        return ToActionResult(
            await _bus.InvokeAsync<Result<PagedResult<CronRunSummaryDto>>>(query, cancellationToken));
    }

    /// <summary>One run and what it printed. A run of another entry or account answers 404.</summary>
    /// <param name="entryId">The entry the run belongs to.</param>
    /// <param name="runId">The run.</param>
    /// <param name="accountId">The account whose entry it is.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{runId:guid}")]
    [ProducesResponseType(typeof(CronRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(
        [FromRoute] string entryId,
        [FromRoute] Guid runId,
        [FromQuery] Guid accountId,
        CancellationToken cancellationToken)
    {
        var query = new GetCronRunQuery(accountId, entryId, runId);

        return ToActionResult(await _bus.InvokeAsync<Result<CronRunDto>>(query, cancellationToken));
    }
}
