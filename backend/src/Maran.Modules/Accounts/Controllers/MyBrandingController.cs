using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetMyBranding;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Accounts.Controllers;

/// <summary>
/// HTTP surface for the branding a customer's shell shows: the name, logo and accent colour of the
/// reseller that owns the account. Thin by design (rules/csharp.md "Controller shape is fixed").
/// </summary>
/// <remarks>
/// A controller of its own for the reason <see cref="MyAccountController"/> gives: a class-level
/// policy is combined with every action's, so the looser <c>AnyAuthenticated</c> read cannot live in a
/// stricter controller. The account is the one the caller's token names; there is no parameter to name
/// another. The reseller sets these values through <see cref="ResellerBrandingController"/>.
/// </remarks>
[Route("api/v1/accounts/me/branding")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Accounts")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class MyBrandingController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public MyBrandingController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Reads the branding the caller's shell shows. Every member is empty when the panel's own applies,
    /// which includes every caller that owns no account or whose account has no reseller.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(ResellerBrandingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<ResellerBrandingDto>>(new GetMyBrandingQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
