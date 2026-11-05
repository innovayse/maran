using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.GetApiKeySelf;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.SharedKernel.Callers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Identity.Controllers;

/// <summary>
/// The one endpoint an API key reaches that is about the key itself: what an integration reads to
/// learn which key it holds and what it may do (issue #144).
/// </summary>
/// <remarks>
/// A separate controller from <see cref="ApiKeysController"/> because authorization metadata at class
/// level is combined with an action's, and this endpoint must accept the <c>ApiKey</c> scheme and
/// NOT the administrator policy. It names the scheme and the scope policy explicitly: a key reaches
/// only endpoints that opt in this way, and no existing policy admits one.
/// </remarks>
[Route("api/v1/api-keys/self")]
[Tags("API Keys")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.SelfRead)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class ApiKeySelfController : BaseApiController
{
    /// <summary>The message bus the query is dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The principal of the current request, an API key.</param>
    /// <param name="bus">The message bus the query is dispatched through.</param>
    public ApiKeySelfController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the calling key's name, prefix, scopes, expiry and last use.</summary>
    /// <remarks>Which key is asking comes from the authenticated principal alone; no parameter names one.</remarks>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiKeySelfDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (CurrentUser.Caller is not ApiKeyCaller key)
        {
            return Unauthorized();
        }

        return ToActionResult(await _bus.InvokeAsync<Result<ApiKeySelfDto>>(new GetApiKeySelfQuery(key.KeyId), cancellationToken));
    }
}
