using Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;
using Maran.Modules.Databases.Commands.CreateDatabaseUser;
using Maran.Modules.Databases.Commands.DropDatabaseUser;
using Maran.Modules.Databases.Commands.ResetDatabaseUserPassword;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Queries.GetDatabaseUser;
using Maran.Modules.Databases.Queries.ListDatabasePrivilegePresets;
using Maran.Modules.Databases.Queries.ListDatabaseUsers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Databases.Controllers;

/// <summary>
/// HTTP surface for the MySQL users of an account and the privileges each holds on the account's own
/// databases. Thin by design (rules/csharp.md "Controller shape is fixed"): binds the request,
/// dispatches through Wolverine, translates the <see cref="Result{T}"/>.
///
/// Open to any signed-in caller, because a user belongs to a customer's account and a customer manages
/// their own. What they can SEE is not decided here: every read and mutation goes through
/// <c>DatabasesDbContext</c>, whose global query filter scopes users, and grants through their user, to
/// the caller's account, so another customer's user or database answers 404, never 403 (spec §8,
/// rules/testing.md item 3). Every user here connects from the server itself only.
/// </summary>
[Route("api/v1/database-users")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Database Users")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[RequiresAccountFeature(AccountFeature.Databases)]
public sealed class DatabaseUsersController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public DatabaseUsersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the database users the caller may see.</summary>
    /// <param name="query">The page, size, search, sort and the optional <c>accountId</c> filter, ANDed with the caller's tenant scope.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<DatabaseUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListDatabaseUsersQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<DatabaseUserDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Reads the closed set of privileges that can be granted and the presets over it, named in the
    /// caller's language. The browser holds neither.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("privilege-presets")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(DatabasePrivilegeCatalogueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPrivilegePresetsAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<DatabasePrivilegeCatalogueDto>>(
            new ListDatabasePrivilegePresetsQuery(), cancellationToken));
    }

    /// <summary>Reads one user with the databases a grant may still be added on. Another customer's user answers 404, not 403.</summary>
    /// <param name="id">The user to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(DatabaseUserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<DatabaseUserDetailDto>>(
            new GetDatabaseUserQuery(id), cancellationToken));
    }

    /// <summary>
    /// Creates a user holding no privileges and returns the generated password, the only time it is shown.
    /// </summary>
    /// <param name="command">
    /// The owning account and the name the customer chose. Bound straight from the body; the two audit
    /// fields it also carries are unbindable by construction, so the stamp below is the only thing that
    /// can fill them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(CreatedDatabaseUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateDatabaseUserCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<CreatedDatabaseUserDto>>(stamped, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/database-users/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Gives the user a new password and returns it once. The only recovery for a lost one, since
    /// nothing keeps a copy. Another customer's user answers 404, not 403.
    /// </summary>
    /// <param name="id">The user to re-credential.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/password")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(DatabaseUserPasswordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ResetDatabaseUserPasswordCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<DatabaseUserPasswordDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Makes the privileges the user holds on one database exactly the set given; an empty set removes
    /// the grant. Another customer's user or database answers 404, not 403.
    /// </summary>
    /// <param name="id">The user whose privileges change.</param>
    /// <param name="databaseId">The database the privileges are held on.</param>
    /// <param name="command">The complete privilege set. The ids and the audit fields are stamped below, never bound.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("{id:guid}/grants/{databaseId:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrivilegesAsync(
        Guid id,
        Guid databaseId,
        [FromBody] ChangeDatabaseUserPrivilegesCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with
        {
            UserId = id,
            DatabaseId = databaseId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result>(stamped, cancellationToken));
    }

    /// <summary>
    /// Drops an additional user and every privilege it holds. A database's dedicated user is refused
    /// with 409: it is dropped with its database. Another customer's user answers 404, not 403.
    /// </summary>
    /// <param name="id">The user to drop.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DropDatabaseUserCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
