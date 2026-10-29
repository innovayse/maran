using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violations of the authorization census: one action per way an endpoint can fail to
/// name its policy.
/// </summary>
/// <remarks>
/// Never composed by the panel. It exists so that
/// <see cref="ControllerAuthorizationDeclarationTests.The_census_names_every_way_an_action_can_fail_to_declare_its_policy"/>
/// has something real to refuse; without it the census is satisfied by an empty answer
/// (rules/README.md "Mechanical enforcement").
/// </remarks>
[Route("architecture-tests/undeclared")]
public sealed class UndeclaredAuthorizationController : ControllerBase
{
    /// <summary>Carries no authorization metadata at all; falls to the fallback policy.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("none")]
    public IActionResult NoAttributeAsync()
    {
        return Ok();
    }

    /// <summary>A bare <c>[Authorize]</c>: names no policy.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize]
    [HttpGet("bare")]
    public IActionResult BareAsync()
    {
        return Ok();
    }

    /// <summary>Restricts by role instead of by named policy.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Roles = "Admin")]
    [HttpGet("roles")]
    public IActionResult RolesAsync()
    {
        return Ok();
    }

    /// <summary>Names a policy the panel does not define (a typo for AdminOnly).</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = "AdminOnIy")]
    [HttpGet("unknown")]
    public IActionResult UnknownPolicyAsync()
    {
        return Ok();
    }

    /// <summary>Anonymous, and listed in no pin.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [AllowAnonymous]
    [HttpGet("anonymous")]
    public IActionResult AnonymousAsync()
    {
        return Ok();
    }
}
