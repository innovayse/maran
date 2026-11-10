using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violation of the anonymous-auth rate-limit rule: an anonymous action that names no
/// rate-limit policy.
/// </summary>
/// <remarks>Never composed by the panel; <see cref="AnonymousAuthRateLimitTests"/> must report it.</remarks>
[Route("architecture-tests/unlimited-anonymous")]
public sealed class UnlimitedAnonymousAuthController : ControllerBase
{
    /// <summary>An anonymous action with no rate limit.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public IActionResult RefreshAsync()
    {
        return Ok();
    }
}
