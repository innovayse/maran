using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers.External;

/// <summary>
/// The one account operation that cannot be undone, for a reseller's API key (issue #7): terminating an
/// account and everything under its home directory.
/// </summary>
/// <remarks>
/// <para>
/// Reached ONLY with the <c>ApiKey</c> scheme and the <see cref="ApiKeyPolicyNames.AccountsDelete"/>
/// policy, a scope of its own so that a key which only provisions and suspends cannot destroy data. It is
/// a controller of its own because the policy sits at class level, one policy per controller, where a
/// reviewer can read it.
/// </para>
/// <para>
/// <b>The final backup is always taken, and a key cannot skip it.</b> The command is built from the route
/// id alone: <c>SkipFinalBackup</c> is not bound from anywhere, so it keeps its default, and the handler's
/// own rule (a caller acting for a reseller who asks to skip it is refused) covers a key as well because
/// <c>ResellerScope</c> reads the key's reseller. A failed final backup refuses the deletion and leaves
/// the account as it was.
/// </para>
/// <para>
/// The <c>Idempotency-Key</c> header is required (400 <c>IdempotencyKeyRequired</c> without it). View-as
/// is refused.
/// </para>
/// </remarks>
[Route("api/v1/reseller-api/accounts")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.AccountsDelete)]
[Tags("Reseller API")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Provisioning)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerKeyAccountTerminationController : BaseApiController
{
    /// <summary>The message bus the command is dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the command is dispatched through.</param>
    public ResellerKeyAccountTerminationController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Removes an account, its system user and everything under its home directory, after taking the
    /// final backup.
    /// </summary>
    /// <param name="id">The account to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("{id:guid}")]
    [Idempotent(Required = true)]
    [ProducesResponseType(typeof(ulong), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TerminateAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteAccountCommand(id, ClientIpAddress, CallerUserAgent);
        var result = await _bus.InvokeAsync<Result<ulong>>(command, cancellationToken);
        return ToActionResult(result);
    }
}
