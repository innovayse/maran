using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The accept-side control for the one way an endpoint admits an API key (issue #144): the
/// <c>ApiKey</c> scheme together with an API key policy, which the authorization census must have
/// nothing to say about.
/// </summary>
/// <remarks>
/// Never composed by the panel. Without it the census could be mutated to refuse every endpoint that
/// names a scheme, and every test that only hands it broken input would stay green.
/// </remarks>
[Route("architecture-tests/declared-api-key")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme, Policy = ApiKeyPolicyNames.ServerRead)]
public sealed class DeclaredApiKeyAuthorizationController : ControllerBase
{
    /// <summary>Inherits the class-level API key declaration.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("status")]
    public IActionResult StatusAsync()
    {
        return Ok();
    }
}
