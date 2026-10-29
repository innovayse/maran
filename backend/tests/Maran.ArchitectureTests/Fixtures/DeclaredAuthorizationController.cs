using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The accept-side control of the authorization census: every legitimate way to declare a policy,
/// which the census must have nothing to say about.
/// </summary>
/// <remarks>
/// Never composed by the panel. A gate that refuses everything passes every test that only ever
/// hands it broken input (rules/testing.md: a refusing gate needs an inverse control).
/// </remarks>
[Route("architecture-tests/declared")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class DeclaredAuthorizationController : ControllerBase
{
    /// <summary>Inherits the class-level administrator policy.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("admin")]
    public IActionResult AdminAsync()
    {
        return Ok();
    }

    /// <summary>Adds the customer-level policy on top of the class-level one.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
    [HttpGet("customer")]
    public IActionResult CustomerAsync()
    {
        return Ok();
    }

    /// <summary>Anonymous; the test pins it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [AllowAnonymous]
    [HttpGet("open")]
    public IActionResult OpenAsync()
    {
        return Ok();
    }
}
