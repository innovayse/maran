using Maran.Modules.Notifications.Commands.SaveSmtpSettings;
using Maran.Modules.Notifications.Commands.SendTestMail;
using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Controllers.Requests;
using Maran.Modules.Notifications.Queries.GetSmtpSettings;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Notifications.Controllers;

/// <summary>
/// HTTP surface for the panel's outgoing mail settings (R12). Thin by design: binds the request,
/// dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Administrators only. The mail settings hold a credential for the operator's own mail provider,
/// and where the panel's alerts go is a server-wide decision with no tenant dimension.
/// </para>
/// <para>
/// <b>The read never returns the password</b> — <c>SmtpSettingsDto</c> has no field for one, which
/// makes the guarantee structural rather than a thing each handler must remember
/// (rules/security.md item 8).
/// </para>
/// </remarks>
[Route("api/v1/notifications/smtp")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Notifications")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class SmtpSettingsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public SmtpSettingsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the panel's mail settings, with a flag in place of the password.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(SmtpSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetSmtpSettingsQuery();
        return ToActionResult(await _bus.InvokeAsync<Result<SmtpSettingsDto>>(query, cancellationToken));
    }

    /// <summary>Replaces the panel's mail settings.</summary>
    /// <param name="request">The settings to save.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// A PUT rather than a POST: there is exactly one settings row on a panel, the request carries
    /// all of it, and repeating the same body twice leaves the panel in the same state.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveAsync(
        [FromBody] SaveSmtpSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SaveSmtpSettingsCommand(
            request.Host,
            request.Port,
            request.Security,
            request.Username,
            request.Password,
            request.FromAddress,
            request.FromName,
            request.AlertRecipient,
            ClientIpAddress,
            UserAgent());

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Sends one fixed test message, so an administrator can see whether the settings work.</summary>
    /// <param name="request">Where to send it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <remarks>
    /// This is the one mail path that reports its failure to a caller. Everywhere else a failed send
    /// is journalled and abandoned because nobody is waiting; here somebody pressed a button
    /// precisely to find out, so the refusal — and its localized reason — is the answer.
    /// </remarks>
    [HttpPost("test")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendTestAsync(
        [FromBody] SendTestMailRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SendTestMailCommand(request.Recipient, ClientIpAddress, UserAgent());
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(command, cancellationToken));
    }

    /// <summary>Reads the caller's user agent for the audit journal.</summary>
    /// <returns>The <c>User-Agent</c> header, or the empty string when absent.</returns>
    private string UserAgent()
    {
        return HttpContext.Request.Headers.UserAgent.ToString();
    }
}
