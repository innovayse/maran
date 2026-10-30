using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.GetProfile;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The signed-in person's own profile, for every role. Scoped to the caller's token — there is no
/// route, query or body parameter naming a user, so the endpoint cannot be pointed at somebody else
/// (rules/security.md item 6).
/// </summary>
[Route("api/v1/profile")]
[Tags("Profile")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class ProfileController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public ProfileController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Returns the caller's profile: who they are and the state of their sign-in security.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var query = new GetProfileQuery(CurrentUser.UserId);
        return ToActionResult(await _bus.InvokeAsync<Result<ProfileDto>>(query, cancellationToken));
    }
}
