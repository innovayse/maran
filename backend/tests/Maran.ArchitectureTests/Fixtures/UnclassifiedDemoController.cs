using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violations of the demo refusal census: a mutating action nobody classified, one that checks a
/// route value its route lacks, an anonymous counted one, and a read that must be left alone.
/// </summary>
/// <remarks>
/// Never composed by the panel. It exists so the census has something real to refuse; without it the walk
/// is satisfied by an empty answer (rules/README.md "Mechanical enforcement").
/// </remarks>
[Route("architecture-tests/demo-unclassified")]
public sealed class UnclassifiedDemoController : ControllerBase
{
    /// <summary>A mutation the planted table does not name.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("missing")]
    public IActionResult MissingAsync()
    {
        return Ok();
    }

    /// <summary>Named with a protected route value the route does not carry.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpDelete("no-id-here")]
    public IActionResult WrongKeyAsync()
    {
        return Ok();
    }

    /// <summary>Anonymous, allowed and counted: a bot could spend the shared budget with it.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [AllowAnonymous]
    [HttpPost("anonymous")]
    public IActionResult AnonymousCountedAsync()
    {
        return Ok();
    }

    /// <summary>A read the table does not name and the census must leave alone.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpGet("read")]
    public IActionResult ReadAsync()
    {
        return Ok();
    }
}
