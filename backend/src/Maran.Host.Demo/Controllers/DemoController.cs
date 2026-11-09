using Maran.Host.Demo.Common;
using Maran.Host.Demo.Reset;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.Demo.Controllers;

/// <summary>
/// The demonstration's one endpoint of its own. Anonymous by necessity, because the login page shows
/// the published sign-ins before anybody has signed in, and it returns nothing that is not public.
/// </summary>
/// <remarks>
/// It reads a provider directly instead of dispatching a message: Wolverine scans the entry assembly
/// for handlers, and a handler declared here would need generated code of its own.
/// </remarks>
[Route("api/v1/demo")]
[Tags("Demo")]
[Produces("application/json")]
public sealed class DemoController : BaseApiController
{
    /// <summary>Builds the status.</summary>
    private readonly DemoStatusProvider _status;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">The principal, anonymous on this controller.</param>
    /// <param name="status">Builds the status.</param>
    public DemoController(ICurrentUser currentUser, DemoStatusProvider status)
        : base(currentUser)
    {
        _status = status;
    }

    /// <summary>Reports the demonstration's cadence, last and next reset, sign-ins and read-only reasons.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The status.</returns>
    [HttpGet("status")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Api)]
    [ProducesResponseType(typeof(DemoStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Ok(await _status.GetAsync(cancellationToken));
    }
}
