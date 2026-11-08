using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The accept-side control of the customer-operation census: an action a customer reaches, which the
/// matrix lists, beside the three kinds of action a customer does not reach and the census must leave out.
/// </summary>
/// <remarks>
/// Never composed by the panel. A gate that refuses everything passes every test that only ever hands it
/// broken input (rules/testing.md: a refusing gate needs an inverse control).
/// </remarks>
[Route("api/v1/architecture-tests/customer-listed")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
public sealed class ListedCustomerController : ControllerBase
{
    /// <summary>A customer reads it; the matrix lists it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("{id:guid}")]
    public IActionResult GetAsync()
    {
        return Ok();
    }

    /// <summary>Administrator-only: a customer's token never reaches it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("admin")]
    public IActionResult AdministratorOnlyAsync()
    {
        return Ok();
    }

    /// <summary>Reseller-only: a customer's token never reaches it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = AuthorizationPolicies.ResellerOnly)]
    [HttpPost("reseller")]
    public IActionResult ResellerOnlyAsync()
    {
        return Ok();
    }

    /// <summary>Anonymous: it reads no token, so it is no operation of a customer's.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [AllowAnonymous]
    [HttpPost("open")]
    public IActionResult OpenAsync()
    {
        return Ok();
    }
}
