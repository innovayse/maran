using Maran.Modules.Mail.Commands.DisableMailServer;
using Maran.Modules.Mail.Commands.EnableMailServer;
using Maran.Modules.Mail.Commands.UpdateMailServerSettings;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Queries.GetMailServerStatus;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Mail.Controllers;

/// <summary>
/// HTTP surface for the host's mail server: the switch, its settings, and what the daemons are
/// actually doing. Thin by design (rules/csharp.md "Controller shape is fixed") — binds, dispatches,
/// translates.
/// </summary>
/// <remarks>
/// <b><c>AdminOnly</c> on the class, not per action.</b> There is one mail stack on this server and
/// switching it affects every account on it, so this is an operator's control. Nothing here is
/// tenant-scoped, so no query filter stands behind it: this policy IS the authorisation. The
/// actions need no view-as declaration and no account-feature gate, because no customer token
/// reaches them.
/// </remarks>
[Route("api/v1/mail-server")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Mail Server")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MailServerController : BaseApiController
{
    /// <summary>The message bus every action dispatches through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The caller, for the base class.</param>
    /// <param name="bus">The message bus.</param>
    public MailServerController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reports what the mail server is configured to do and what it is observed to do.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(MailServerStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetMailServerStatusQuery();

        return ToActionResult(await _bus.InvokeAsync<Result<MailServerStatusDto>>(query, cancellationToken));
    }

    /// <summary>Turns the mail server on with the given settings.</summary>
    /// <param name="command">The settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status after the daemons were configured.</returns>
    [HttpPost("enable")]
    [ProducesResponseType(typeof(MailServerStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> EnableAsync(
        [FromBody] EnableMailServerCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MailServerStatusDto>>(command, cancellationToken));
    }

    /// <summary>Turns the mail server off; mail data and accounts' mail settings stay.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status after the daemons were stopped.</returns>
    [HttpPost("disable")]
    [ProducesResponseType(typeof(MailServerStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DisableAsync(CancellationToken cancellationToken)
    {
        var command = new DisableMailServerCommand(ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<MailServerStatusDto>>(command, cancellationToken));
    }

    /// <summary>Re-states the settings of a mail server that is on.</summary>
    /// <param name="command">The settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status after the daemons were reconfigured.</returns>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(MailServerStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateSettingsAsync(
        [FromBody] UpdateMailServerSettingsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<MailServerStatusDto>>(command, cancellationToken));
    }
}
