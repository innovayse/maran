using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violations of the view-as census: one action per way an endpoint a view-as token can
/// reach can fail to be classified, plus two undeclared actions the census must leave alone because no
/// view-as token reaches them.
/// </summary>
/// <remarks>
/// Never composed by the panel. It exists so that
/// <see cref="ViewAsAccessDeclarationTests.The_census_names_every_way_an_action_can_fail_to_be_classified"/>
/// has something real to refuse; without it the census is satisfied by an empty answer
/// (rules/README.md "Mechanical enforcement").
/// </remarks>
[Route("architecture-tests/view-as-undeclared")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
public sealed class UndeclaredViewAsController : ControllerBase
{
    /// <summary>Declares nothing about a view-as token.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("none")]
    public IActionResult NoDeclarationAsync()
    {
        return Ok();
    }

    /// <summary>Declares a POST a read.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [ViewAsAccess(ViewAsAccess.Read)]
    [HttpPost("read-post")]
    public IActionResult ReadOnAPostAsync()
    {
        return Ok();
    }

    /// <summary>Declares a GET a mutation.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [ViewAsAccess(ViewAsAccess.SafeMutation)]
    [HttpGet("mutation-get")]
    public IActionResult MutationOnAGetAsync()
    {
        return Ok();
    }

    /// <summary>Declares a safe mutation that no pin lists.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [ViewAsAccess(ViewAsAccess.SafeMutation)]
    [HttpPost("unpinned")]
    public IActionResult UnpinnedMutationAsync()
    {
        return Ok();
    }

    /// <summary>Administrator-only and undeclared: a view-as token carries the customer role and never reaches it.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("admin")]
    public IActionResult AdministratorOnlyAsync()
    {
        return Ok();
    }

    /// <summary>Anonymous and undeclared: the endpoint reads no token, so there is no view-as token to classify.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [AllowAnonymous]
    [HttpPost("open")]
    public IActionResult OpenAsync()
    {
        return Ok();
    }
}
