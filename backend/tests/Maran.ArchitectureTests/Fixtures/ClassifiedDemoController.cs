using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>Every legitimate way of classifying a mutating action, for the census's accept-side control.</summary>
[Route("architecture-tests/demo-classified")]
public sealed class ClassifiedDemoController : ControllerBase
{
    /// <summary>Refused.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("refused")]
    public IActionResult RefusedAsync()
    {
        return Ok();
    }

    /// <summary>Allowed unless the <c>id</c> in its route is protected.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpDelete("{id:guid}")]
    public IActionResult ProtectedCheckAsync()
    {
        return Ok();
    }

    /// <summary>Anonymous but uncounted, like sign-in.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [AllowAnonymous]
    [HttpPost("sign-in")]
    public IActionResult AnonymousUncountedAsync()
    {
        return Ok();
    }

    /// <summary>Plainly allowed.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPut("allowed")]
    public IActionResult AllowedAsync()
    {
        return Ok();
    }
}
