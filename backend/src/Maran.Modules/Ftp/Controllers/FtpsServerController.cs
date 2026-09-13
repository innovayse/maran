using Maran.Modules.Ftp.Commands.DisableFtps;
using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Queries.GetFtpsStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Ftp.Controllers;

/// <summary>
/// HTTP surface for the server's own FTPS daemon: the switch, and what the daemon is actually doing.
/// Thin by design (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// <b><c>AdminOnly</c> on the class, not per action, and not <c>AnyAuthenticated</c>.</b> There is
/// one FTPS daemon on this server and switching it affects every account on it, so this is an
/// operator's control rather than a customer's. Nothing here is tenant-scoped, which means there is
/// no query filter behind it either: this policy IS the authorisation, and weakening it would hand a
/// customer the ability to stop file transfer for everybody. The status is admin-only for a second
/// reason — it carries a certificate path on the host, which is operator-facing text a customer must
/// never be shown (rules/security.md item 8).
/// </remarks>
[Route("api/v1/ftps-server")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("FTPS Server")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class FtpsServerController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FtpsServerController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reports what the FTPS daemon is doing, measured on the host rather than recalled.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(FtpsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetFtpsStatusQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<FtpsStatusDto>>(query, cancellationToken));
    }

    /// <summary>Configures the daemon for a hostname this panel serves and brings it up.</summary>
    /// <param name="command">The hostname, and the PASV address for a host behind NAT.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A 500 here is the honest answer for a server with no certificate material for the hostname:
    /// the request is not malformed and retyping it changes nothing, so the operator is sent to the
    /// SSL section by the message rather than back to this form by the status.
    /// </remarks>
    [HttpPost("enable")]
    [ProducesResponseType(typeof(FtpsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> EnableAsync(
        [FromBody] EnableFtpsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FtpsStatusDto>>(command, cancellationToken));
    }

    /// <summary>Stops the daemon. Customer logins are left exactly as they are.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// There is no body to bind, so the command is CONSTRUCTED from the server-established values
    /// rather than deserialized. The guard attributes stay on the command regardless — they are a
    /// property of the type, so a command that becomes body-bound later is already safe.
    /// </remarks>
    [HttpPost("disable")]
    [ProducesResponseType(typeof(FtpsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DisableAsync(CancellationToken cancellationToken)
    {
        var command = new DisableFtpsCommand(ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<FtpsStatusDto>>(command, cancellationToken));
    }
}
