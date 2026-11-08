using Maran.Samples.Hello.Common;
using Maran.Samples.Hello.Queries.GetGreeting;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Samples.Hello.Controllers;

/// <summary>
/// The sample module's HTTP surface. Thin by design (rules/csharp.md "Controller shape is fixed"): binds the
/// request, dispatches through Wolverine, translates the <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Administrators only, through the one policy the manifest declares in <see cref="Manifest.Permissions"/>.
/// A policy named here and absent from the manifest fails the module contract test.
/// </remarks>
[Route("api/v1/hello")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Tags("Hello")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public sealed class HelloController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public HelloController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Answers with the newest greeting.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(GreetingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return ToActionResult(await _bus.InvokeAsync<Result<GreetingDto>>(new GetGreetingQuery(), cancellationToken));
    }
}
