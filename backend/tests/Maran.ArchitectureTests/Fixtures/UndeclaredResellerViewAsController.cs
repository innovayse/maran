using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violations of the view-as census for a reseller-kind token: two undeclared actions a
/// token holding the reseller role reaches, plus one undeclared action it does not.
/// </summary>
/// <remarks>
/// Never composed by the panel. An administrator viewing as a reseller holds a token whose role is
/// <c>Reseller</c>, so an action naming the admin-or-reseller or the reseller-only policy is
/// reachable by a view-as token and must declare what that token may do there. A census that read
/// "a view-as token is a customer" would skip both; the control is what shows it does not.
/// </remarks>
[Route("architecture-tests/view-as-undeclared-reseller")]
[Authorize(Policy = AuthorizationPolicies.AdminOrReseller)]
public sealed class UndeclaredResellerViewAsController : ControllerBase
{
    /// <summary>Admin-or-reseller by its class and undeclared: a reseller-kind view-as token reaches it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("shared")]
    public IActionResult SharedAsync()
    {
        return Ok();
    }

    /// <summary>Reseller-only and undeclared: a reseller-kind view-as token reaches it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = AuthorizationPolicies.ResellerOnly)]
    [HttpGet("own")]
    public IActionResult OwnAsync()
    {
        return Ok();
    }

    /// <summary>Administrator-only and undeclared: no view-as token holds the administrator role.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("admin")]
    public IActionResult AdministratorOnlyAsync()
    {
        return Ok();
    }
}
