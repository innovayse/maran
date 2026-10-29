using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violation of the class-level anonymity rule: <c>[AllowAnonymous]</c> on the type,
/// above an action that declares a policy and is therefore silently opened to everyone.
/// </summary>
/// <remarks>
/// Never composed by the panel. It is the planted controller that
/// <see cref="ControllerAuthorizationCensus.FindClassLevelAnonymity"/> must report.
/// </remarks>
[Route("architecture-tests/class-anonymous")]
[AllowAnonymous]
public sealed class ClassLevelAnonymousController : ControllerBase
{
    /// <summary>Declares a policy that the class-level attribute outranks.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
#pragma warning disable ASP0026 // The overridden [Authorize] is the planted defect this fixture models.
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
#pragma warning restore ASP0026
    [HttpGet("secret")]
    public IActionResult SecretAsync()
    {
        return Ok();
    }
}
