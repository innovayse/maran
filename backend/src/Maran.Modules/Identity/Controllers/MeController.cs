using Maran.Modules.Identity.Commands.SwitchActiveAccount;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Queries.ListMyAccounts;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The signed-in person's own hosting accounts: which ones they may act for, and which one this
/// sign-in does. Scoped to the caller's token — the account list names no user, and the switch names
/// only the account to move to, so neither can be pointed at somebody else's login
/// (rules/security.md item 6).
/// </summary>
[Route("api/v1/me")]
[Tags("Me")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MeController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public MeController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists the hosting accounts the caller holds a membership of, marking the active one.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("accounts")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<SwitchableAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var principal = SignedInCallerPolicy.Require(CurrentUser);
        Guid? accountId = principal is CustomerCaller customer ? customer.AccountId : null;
        var query = new ListMyAccountsQuery(principal.UserId, accountId);
        return ToActionResult(await _bus.InvokeAsync<Result<IReadOnlyList<SwitchableAccountDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Makes the caller's current sign-in act for another account they hold a membership of, and
    /// returns the access token that speaks for it. The refresh cookie is unchanged.
    /// </summary>
    /// <remarks>
    /// An account the caller holds no membership of answers 404, not 403: 403 would confirm that the
    /// account exists, which is enough to enumerate other people's accounts.
    /// </remarks>
    /// <param name="command">The account to switch to, bound from the body.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("active-account")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(ActiveAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchActiveAccountAsync(
        [FromBody] SwitchActiveAccountCommand command,
        CancellationToken cancellationToken)
    {
        command = command with
        {
            UserId = SignedInCallerPolicy.Require(CurrentUser).UserId,
            SessionId = CallerSessionId,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<ActiveAccountDto>>(command, cancellationToken));
    }
}
