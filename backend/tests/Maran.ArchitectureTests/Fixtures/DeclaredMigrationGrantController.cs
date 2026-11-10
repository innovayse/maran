using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>A controller that declares the one way an export grant is admitted: the grant scheme with the grant's pull policy (issue #127).</summary>
[Route("architecture-tests/declared-migration-grant")]
[Authorize(AuthenticationSchemes = MigrationGrantDefaults.Scheme, Policy = MigrationGrantPolicyNames.Pull)]
public sealed class DeclaredMigrationGrantController : ControllerBase
{
    /// <summary>Reads something, as a pull endpoint does.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("head")]
    public IActionResult HeadAsync()
    {
        return Ok();
    }
}
