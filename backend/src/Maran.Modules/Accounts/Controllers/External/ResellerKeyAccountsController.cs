using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers.External;

/// <summary>
/// The accounts a reseller's API key may read (issue #368): the reseller's own hosting accounts, listed
/// for an integration that provisions on its behalf.
/// </summary>
/// <remarks>
/// <para>
/// Reached ONLY with the <c>ApiKey</c> scheme and the <see cref="ApiKeyPolicyNames.AccountsRead"/> policy,
/// which admits a key a reseller owns and holds that scope, and no signed-in login: the panel's own
/// screens use <see cref="AccountsController"/>. Which accounts the key sees is decided below this
/// controller, by <c>AccountsDbContext</c>, which narrows a reseller's key to the accounts that reseller
/// owns; an account another reseller owns is not in the page and cannot be named into it.
/// </para>
/// <para>
/// Read-only and list-only: no action dispatches a command, and there is no read of a single account,
/// whose detail carries the owner's e-mail address, which a key has no business holding.
/// </para>
/// </remarks>
[Route("api/v1/reseller-api/accounts")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.AccountsRead)]
[Tags("Reseller API")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ResellerKeyAccountsController : BaseApiController
{
    /// <summary>The message bus the query is dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the query is dispatched through.</param>
    public ResellerKeyAccountsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Lists one page of the key's reseller's accounts, filtered by plan and status, searched by name or domain, and sorted.</summary>
    /// <param name="query">Paging, filters, search and sort, bound from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync([FromQuery] ListAccountsQuery query, CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<PagedResult<AccountDto>>>(query, cancellationToken);
        return ToActionResult(result);
    }
}
