using Maran.Modules.Sites.Commands.ReplacePhpSettings;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Queries.GetPhpSettings;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Sites.Controllers;

/// <summary>
/// HTTP surface for the customer's PHP settings (issue #138). Thin by design (rules/csharp.md
/// "Controller shape is fixed"): stamps the caller's own account, dispatches through Wolverine and
/// translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Open to a signed-in caller whose plan sells the Php feature and, for a team member, whose owner
/// gave it to them, like switching a site's PHP version. The account is always the caller's own: the
/// action reads it from the caller and the command carries it as a server-established member, so there
/// is no request that can name another customer's account. A caller who is not a customer of an
/// account gets the same 404 as a customer asking after an account that is not theirs.
/// </remarks>
[Route("api/v1/php-settings")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[RequiresAccountFeature(AccountFeature.Php)]
[Tags("PHP settings")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class PhpSettingsController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>
    /// The account the caller acts for, or the empty id for anyone who is not a customer of one, which
    /// no account has and so reads as not found.
    /// </summary>
    private Guid OwnAccountId
    {
        get
        {
            return CurrentUser.Caller is CustomerCaller customer ? customer.AccountId : Guid.Empty;
        }
    }

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public PhpSettingsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the caller's PHP settings with the plan's bounds beside each.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PhpSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetPhpSettingsQuery(OwnAccountId);
        return ToActionResult(await _bus.InvokeAsync<Result<PhpSettingsDto>>(query, cancellationToken));
    }

    /// <summary>
    /// Replaces the caller's PHP settings for every pool their account has. A full replacement, and
    /// repeating it changes nothing.
    /// </summary>
    /// <param name="command">The settings to keep; one not listed goes back to PHP's default.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(PhpSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplaceAsync(
        [FromBody] ReplacePhpSettingsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { AccountId = OwnAccountId, IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };
        return ToActionResult(await _bus.InvokeAsync<Result<PhpSettingsDto>>(command, cancellationToken));
    }
}
