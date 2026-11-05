using Maran.Modules.Sftp.Commands.AddSftpUserKey;
using Maran.Modules.Sftp.Commands.CreateSftpUser;
using Maran.Modules.Sftp.Commands.DeleteSftpUser;
using Maran.Modules.Sftp.Commands.RemoveSftpUserKey;
using Maran.Modules.Sftp.Commands.ResetSftpUserPassword;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Queries.GetSftpUser;
using Maran.Modules.Sftp.Queries.ListSftpUserKeys;
using Maran.Modules.Sftp.Queries.ListSftpUsers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
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
[RequiresAccountFeature(AccountFeature.FileTransfer)]
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

    /// <summary>Lists one page of the SFTP logins the caller may see.</summary>
    /// <param name="query">
    /// Paging, search and sort, and the <c>accountId</c> filter, from the query string; the filter is
    /// ANDed with the caller's tenant scope.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<SftpUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListSftpUsersQuery query,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<SftpUserDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one SFTP login. Another customer's login answers 404, not 403.</summary>
    /// <param name="id">The login to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Read)]
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
    /// <param name="command">The owning account and the name the customer chose.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(CreatedSftpUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSftpUserCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

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
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(SftpUserPasswordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ResetSftpUserPasswordCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<SftpUserPasswordDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Removes the login, and only the login: the account's files stay exactly where they are.
    /// Another customer's login answers 404, not 403.
    /// </summary>
    /// <param name="id">The login to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteSftpUserCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }

    /// <summary>
    /// Lists one page of the public keys registered for one SFTP login: label, algorithm, fingerprint
    /// and when each was added — never the key itself. Another customer's login answers 404, not 403.
    /// </summary>
    /// <param name="id">The login whose keys to list.</param>
    /// <param name="query">Paging, search by label and sort, from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}/keys")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<SftpUserKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKeysAsync(
        Guid id,
        [FromQuery] ListSftpUserKeysQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { SftpUserId = id };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<SftpUserKeyDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Registers a public key for one SFTP login, so the login can be signed into with the matching
    /// private key. Another customer's login answers 404, not 403.
    /// </summary>
    /// <param name="id">The login the key is for.</param>
    /// <param name="command">The label and the pasted key.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/keys")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(SftpUserKeyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AddKeyAsync(
        Guid id,
        [FromBody] AddSftpUserKeyCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { SftpUserId = id, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<SftpUserKeyDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/sftp-users/{id}/keys/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Removes one public key from a login and ends that login's open sessions, including ones that
    /// signed in another way. Another customer's login or key answers 404, not 403.
    /// </summary>
    /// <param name="id">The login the key belongs to.</param>
    /// <param name="keyId">The key to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}/keys/{keyId:guid}")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveKeyAsync(Guid id, Guid keyId, CancellationToken cancellationToken)
    {
        var command = new RemoveSftpUserKeyCommand(id, keyId, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result>(command, cancellationToken));
    }
}
