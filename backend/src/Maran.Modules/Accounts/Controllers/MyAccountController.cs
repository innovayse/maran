using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetMyAccount;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for a customer's own view of the account they own: plan, limits, status (spec §8).
/// Thin by design (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// <b>A controller of its own, not an action added to <see cref="AccountsController"/>.</b>
/// <see cref="AccountsController"/> carries <c>[Authorize(Policy = AuthorizationPolicies.AdminOnly)]</c>
/// at CLASS level, and ASP.NET Core combines every <c>[Authorize]</c> attribute on a chain with AND,
/// never override: an action there marked <c>AnyAuthenticated</c> would still require the class's
/// <c>AdminOnly</c> policy too, so a customer would still be refused and the endpoint would answer
/// nobody but the administrators who already have the whole list. There is no attribute that opens a
/// hole in a stricter class-level policy from an action below it, so the only way to grant a
/// customer this read is a controller whose own class-level policy is the looser one.
/// </remarks>
[Route("api/v1/accounts/me")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Accounts")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MyAccountController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public MyAccountController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads the account the caller owns: its identity, status, and its plan's limits. An
    /// administrator — who owns no account — gets the same 404 as anybody else without one
    /// (see <see cref="GetMyAccountQueryHandler"/>).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(MyAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<MyAccountDto>>(new GetMyAccountQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
