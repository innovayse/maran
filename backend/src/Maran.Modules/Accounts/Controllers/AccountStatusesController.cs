using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.ListAccountStatusChoices;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for the names of the states a hosting account can be in. Thin by design
/// (rules/csharp.md "Controller shape is fixed") — binds, dispatches, translates.
/// </summary>
/// <remarks>
/// <b>A controller of its own, readable by any signed-in caller.</b> The administrator's accounts
/// list offers the states as a filter, and a customer's own account page prints its state with the
/// same name. <see cref="AccountsController"/> carries <c>AdminOnly</c> at class level and ASP.NET
/// Core combines every <c>[Authorize]</c> on a chain with AND, so an action there could not open the
/// read to a customer (see <see cref="MyAccountController"/>). The set is a closed vocabulary tied
/// to no row, so it discloses nothing about any account.
/// </remarks>
[Route("api/v1/accounts/statuses")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Accounts")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class AccountStatusesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public AccountStatusesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists every state a hosting account can be in, each named in the caller's language.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<AccountStatusChoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<IReadOnlyList<AccountStatusChoiceDto>>>(
            new ListAccountStatusChoicesQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
