using Microsoft.AspNetCore.Mvc;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A controller that carries NO authorization metadata of any kind — no <c>[Authorize]</c>, no
/// <c>[AllowAnonymous]</c> — so the only rule that can decide who may call it is the host's
/// fallback policy.
/// </summary>
/// <remarks>
/// <para>
/// It stands in for the code the fallback exists for: a controller written by a third party, or
/// forgotten by a contributor, that the host nevertheless maps. It is added to a host by the one
/// test class that needs it (<see cref="AuthorizationFallbackEndpointTests"/>) and to no other, so
/// the route-table census never sees it.
/// </para>
/// <para>
/// Adding any authorization attribute here, or to a base class, turns every test that uses it into
/// a test of that attribute instead of the fallback; the class's precondition test reads the
/// endpoint's metadata for exactly that reason.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/test-probes/unmarked")]
public sealed class UnmarkedProbeController : ControllerBase
{
    /// <summary>Answers 200 to whoever the pipeline lets through.</summary>
    /// <returns>An empty 200.</returns>
    [HttpGet]
    public IActionResult Get()
    {
        return Ok();
    }
}
