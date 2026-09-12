using Maran.Modules.Ftp.Commands.CreateFtpUser;
using Maran.Modules.Ftp.Commands.DeleteFtpUser;
using Maran.Modules.Ftp.Commands.ResetFtpUserPassword;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Queries.GetFtpUser;
using Maran.Modules.Ftp.Queries.ListFtpUsers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Ftp.Controllers;

/// <summary>
/// HTTP surface for customer FTPS logins. Thin by design (rules/csharp.md "Controller shape is
/// fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// No business logic, no data access.
///
/// Open to any signed-in caller, because a login belongs to a customer and a customer manages their
/// own — unlike <c>FtpsServerController</c> beside it, which switches the server's daemon on and is
/// <c>AdminOnly</c>. What a caller can SEE is not decided here: every read and every mutation goes
/// through <c>FtpDbContext</c>, whose global query filter scopes login rows to the caller's account,
/// so a login belonging to somebody else answers 404 — never 403, which would confirm it exists
/// (spec §8, rules/testing.md item 3). No route on this controller takes a login NAME, only a row
/// id, so there is nothing here a caller could aim at a neighbour's login by spelling it.
/// </summary>
[Route("api/v1/ftp-users")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("FTPS Users")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class FtpUsersController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FtpUsersController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the FTPS logins the caller may see.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FtpUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken)
    {
        var query = new ListFtpUsersQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<FtpUserDto>>>(query, cancellationToken));
    }

    /// <summary>Reads one FTPS login. Another customer's login answers 404, not 403.</summary>
    /// <param name="id">The login to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FtpUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<FtpUserDto>>(new GetFtpUserQuery(id), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates an FTPS login and returns the generated password — the only time it is ever shown.
    /// </summary>
    /// <param name="command">The owning account and the name the customer chose.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedFtpUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateFtpUserCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var result = await _bus.InvokeAsync<Result<CreatedFtpUserDto>>(command, cancellationToken);
        return ToCreatedActionResult(
            result, $"/api/v1/ftp-users/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>
    /// Gives the login a new password and returns it once. The only recovery for a lost one, since
    /// nothing keeps a copy. Another customer's login answers 404, not 403.
    /// </summary>
    /// <param name="id">The login to re-credential.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType(typeof(FtpUserPasswordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new ResetFtpUserPasswordCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<FtpUserPasswordDto>>(command, cancellationToken));
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
        var command = new DeleteFtpUserCommand(id, ClientIpAddress, CallerUserAgent);
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }
}
