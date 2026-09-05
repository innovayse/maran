using Maran.Modules.Sftp.Commands.CreateSftpUser;
using Maran.Modules.Sftp.Commands.DeleteSftpUser;
using Maran.Modules.Sftp.Commands.ResetSftpUserPassword;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Controllers.Requests;
using Maran.Modules.Sftp.Queries.GetSftpUser;
using Maran.Modules.Sftp.Queries.ListSftpUsers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Sftp.Controllers;

/// <summary>
/// HTTP surface for customer SFTP logins. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// No business logic, no data access.
///
/// Open to any signed-in caller, because a login belongs to a customer and a customer manages their
/// own. What they can SEE is not decided here: every read and every mutation goes through
/// <c>SftpDbContext</c>, whose global query filter scopes rows to the caller's account, so a login
/// belonging to somebody else answers 404 — never 403, which would confirm it exists (spec §8,
/// rules/testing.md item 3).
/// </summary>
[Route("api/v1/sftp-users")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("SFTP Users")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SftpUsersController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public SftpUsersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the SFTP logins the caller may see.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SftpUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListSftpUsersQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<SftpUserDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one SFTP login. Another customer's login answers 404, not 403.</summary>
    /// <param name="id">The login to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SftpUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<SftpUserDto>>(new GetSftpUserQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates an SFTP login and returns the generated password — the only time it is ever shown.
    /// </summary>
    /// <param name="request">The owning account and the name the customer chose.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedSftpUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateSftpUserCommand(request.AccountId, request.Name, ClientIpAddress, UserAgent());

        var result = await _bus.InvokeAsync<Result<CreatedSftpUserDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/sftp-users/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Gives the login a new password and returns it once. The only recovery for a lost one, since
    /// nothing keeps a copy. Another customer's login answers 404, not 403.
    /// </summary>
    /// <param name="id">The login to re-credential.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType(typeof(SftpUserPasswordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ResetSftpUserPasswordCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<SftpUserPasswordDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Removes the login, and only the login: the account's files stay exactly where they are.
    /// Another customer's login answers 404, not 403.
    /// </summary>
    /// <param name="id">The login to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteSftpUserCommand(id, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
