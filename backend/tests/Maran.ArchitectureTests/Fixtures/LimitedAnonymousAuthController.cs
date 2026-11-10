using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The accepting control of the anonymous-auth rate-limit rule: an anonymous action that names a policy.
/// </summary>
/// <remarks>Never composed by the panel; <see cref="AnonymousAuthRateLimitTests"/> must leave it alone.</remarks>
[Route("architecture-tests/limited-anonymous")]
public sealed class LimitedAnonymousAuthController : ControllerBase
{
    /// <summary>An anonymous action with a rate limit.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.SessionRefresh)]
    public IActionResult RefreshAsync()
    {
        return Ok();
    }
}
