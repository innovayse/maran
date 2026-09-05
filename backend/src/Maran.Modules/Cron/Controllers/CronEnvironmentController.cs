using Maran.Modules.Cron.Commands.SetCronEnvironment;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Controllers.Requests;
using Maran.Modules.Cron.Queries.GetCronEnvironment;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Cron.Controllers;

/// <summary>
/// HTTP surface for the environment assignments the agent manages in an account's crontab. Thin by
/// design (rules/csharp.md "Controller shape is fixed").
///
/// A controller of its own rather than two more routes on the entries controller, because the
/// environment is a property of the CRONTAB and not of any entry: it has no entry id, it is read and
/// written whole, and hanging it off an entries route would suggest a relationship to one entry that
/// does not exist.
///
/// Open to any signed-in caller, and scoped exactly as the entries are: the account is named by row
/// id and resolved in the handler, so another customer's account answers 404 and never 403.
/// </summary>
[Route("api/v1/cron-environment")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Cron")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class CronEnvironmentController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public CronEnvironmentController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads one account's managed environment assignments. Another customer's account answers 404,
    /// not 403.
    /// </summary>
    /// <param name="accountId">The account whose crontab to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CronEnvironmentVariableDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var query = new GetCronEnvironmentQuery(accountId);
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<CronEnvironmentVariableDto>>>(
            query, cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Replaces the managed assignments with exactly the set sent. A name absent from the body is
    /// removed, and an empty list clears them all. Another customer's account answers 404, not 403.
    /// </summary>
    /// <param name="request">The owning account and the complete new set.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAsync(
        [FromBody] SetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SetCronEnvironmentCommand(
            request.AccountId, request.Variables, ClientIpAddress, UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
