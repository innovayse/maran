using Maran.Modules.Databases.Commands.CreateDatabase;
using Maran.Modules.Databases.Commands.DropDatabase;
using Maran.Modules.Databases.Commands.ResetDatabasePassword;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Controllers.Requests;
using Maran.Modules.Databases.Queries.GetDatabase;
using Maran.Modules.Databases.Queries.ListDatabases;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Databases.Controllers;

/// <summary>
/// HTTP surface for customer MySQL databases. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// No business logic, no data access.
///
/// Open to any signed-in caller, because a database belongs to a customer and a customer manages
/// their own. What they can SEE is not decided here: every read and every mutation goes through
/// <c>DatabasesDbContext</c>, whose global query filter scopes rows to the caller's account, so a
/// database belonging to somebody else answers 404 — never 403, which would confirm it exists
/// (spec §8, rules/testing.md item 3).
/// </summary>
[Route("api/v1/databases")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Databases")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class DatabasesController : BaseApiController
{
    /// <summary>Recorded when the connection reports no remote address, as in a test host.</summary>
    private const string UnknownIpAddress = "unknown";

    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DatabasesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the databases the caller may see.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DatabaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListDatabasesQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<DatabaseDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one database. Another customer's database answers 404, not 403.</summary>
    /// <param name="id">The database to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DatabaseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<DatabaseDto>>(new GetDatabaseQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates a database and its dedicated user, and returns the generated password — the only time
    /// it is ever shown.
    /// </summary>
    /// <param name="request">The owning account and the two names the customer chose.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedDatabaseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateDatabaseCommand(
            request.AccountId, request.Name, request.DbUserName, IpAddress(), UserAgent());

        var result = await _bus.InvokeAsync<Result<CreatedDatabaseDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/databases/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Gives the database's user a new password and returns it once. The only recovery for a lost
    /// one, since nothing keeps a copy. Another customer's database answers 404, not 403.
    /// </summary>
    /// <param name="id">The database whose user to re-credential.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType(typeof(DatabasePasswordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ResetDatabasePasswordCommand(id, IpAddress(), UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<DatabasePasswordDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Drops the database and its dedicated user. The customer's data goes with it and is not
    /// recoverable. Another customer's database answers 404, not 403.
    /// </summary>
    /// <param name="id">The database to drop.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DropDatabaseCommand(id, IpAddress(), UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's address from the connection, never from a header a caller controls.</summary>
    /// <returns>The remote address, or <see cref="UnknownIpAddress"/> when the connection reports none.</returns>
    private string IpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownIpAddress;
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
