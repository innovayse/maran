using Maran.Sdk.Contracts;
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

    /// <summary>Names the <c>ApiKey</c> scheme with a policy that is not an API key policy.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("api-key-scheme-wrong-policy")]
    public IActionResult ApiKeySchemeWithWrongPolicyAsync()
    {
        return Ok();
    }

    /// <summary>Names an API key policy without the <c>ApiKey</c> scheme.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = ApiKeyPolicyNames.ServerRead)]
    [HttpGet("api-key-policy-without-scheme")]
    public IActionResult ApiKeyPolicyWithoutSchemeAsync()
    {
        return Ok();
    }

    /// <summary>Names the <c>MigrationGrant</c> scheme with a policy that is not the grant's.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(AuthenticationSchemes = MigrationGrantDefaults.Scheme, Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("grant-scheme-wrong-policy")]
    public IActionResult GrantSchemeWithWrongPolicyAsync()
    {
        return Ok();
    }

    /// <summary>Names the grant's policy without the <c>MigrationGrant</c> scheme.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = MigrationGrantPolicyNames.Pull)]
    [HttpGet("grant-policy-without-scheme")]
    public IActionResult GrantPolicyWithoutSchemeAsync()
    {
        return Ok();
    }
}
