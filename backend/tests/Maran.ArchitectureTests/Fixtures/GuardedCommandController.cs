using Maran.ArchitectureTests.Fixtures.Commands.Guarded;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The controller half of the accept-side control: an endpoint binding a fully guarded command from
/// the query string, which is the pipeline the property-target attributes do not reach.
/// </summary>
/// <remarks>
/// Never composed by the panel — see <see cref="GuardedCommand"/>. It exists so that
/// <see cref="BoundCommandGuardTests.The_rule_accepts_a_correctly_guarded_command"/> has something
/// real for the rule to pass.
/// </remarks>
[Route("architecture-tests/guarded-widgets")]
public sealed class GuardedCommandController : ControllerBase
{
    /// <summary>The action the rule must NOT name.</summary>
    /// <param name="command">The fully guarded command, bound whole from the query string.</param>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpGet("{widgetId:guid}")]
    public IActionResult SafeAsync([FromQuery] GuardedCommand command)
    {
        return Ok(command.WidgetId);
    }
}
