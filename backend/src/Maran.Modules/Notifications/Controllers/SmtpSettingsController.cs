using Maran.Modules.Notifications.Commands.SaveSmtpSettings;
using Maran.Modules.Notifications.Commands.SendTestMail;
using Maran.Modules.Notifications.Common;
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
    /// <param name="command">
    /// The settings to save. The command is bound straight from the body — there is no request type
    /// in between — and the two audit fields it also carries are unbindable by construction (see
    /// <see cref="SaveSmtpSettingsCommand"/>), so the stamp below is the only thing that can fill
    /// them. A body that omits <c>password</c> still deserializes it to <c>null</c>, which still
    /// means "keep the stored one".
    /// </param>
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
        [FromBody] SaveSmtpSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(stamped, cancellationToken));
    }

    /// <summary>Sends one fixed test message, so an administrator can see whether the settings work.</summary>
    /// <param name="command">
    /// Where to send it. The command is bound straight from the body — there is no request type in
    /// between — and its two audit fields are unbindable by construction (see
    /// <see cref="SendTestMailCommand"/>).
    /// </param>
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
        [FromBody] SendTestMailCommand command,
        CancellationToken cancellationToken)
    {
        var stamped = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        return ToActionResult(await _bus.InvokeAsync<Result<bool>>(stamped, cancellationToken));
    }
}
