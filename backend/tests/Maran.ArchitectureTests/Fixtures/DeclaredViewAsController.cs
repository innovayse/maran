using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The accept-side control of the view-as census: every legitimate way to classify an action, which
/// the census must have nothing to say about.
/// </summary>
/// <remarks>
/// Never composed by the panel. A gate that refuses everything passes every test that only ever
/// hands it broken input (rules/testing.md: a refusing gate needs an inverse control).
/// </remarks>
[Route("architecture-tests/view-as-declared")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[ViewAsAccess(ViewAsAccess.Refused)]
public sealed class DeclaredViewAsController : ControllerBase
{
    /// <summary>Inherits the class-level refusal.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpPost("refused")]
    public IActionResult RefusedByClassAsync()
    {
        return Ok();
    }

    /// <summary>A read, which overrides the class-level refusal.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [ViewAsAccess(ViewAsAccess.Read)]
    [HttpGet("read")]
    public IActionResult ReadAsync()
    {
        return Ok();
    }

    /// <summary>A safe mutation the test pins.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [ViewAsAccess(ViewAsAccess.SafeMutation)]
    [HttpPost("pinned")]
    public IActionResult PinnedMutationAsync()
    {
        return Ok();
    }
}
