using Maran.ArchitectureTests.Fixtures.Commands.Unguarded;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The controller half of the planted violation: an endpoint whose URL names the widget and whose
/// body could name a different one.
/// </summary>
/// <remarks>
/// Never composed by the panel — see <see cref="UnguardedCommand"/>. It exists so that
/// <see cref="BoundCommandGuardTests.The_rule_names_a_planted_violation"/> has something real to
/// refuse, which is the only way to know the rule refuses anything at all (rules/testing.md: a
/// refusing gate needs an inverse control).
/// </remarks>
[Route("architecture-tests/widgets")]
public sealed class UnguardedCommandController : ControllerBase
{
    /// <summary>The action the rule must name.</summary>
    /// <param name="command">The unguarded command, bound whole from the request body.</param>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpPut("{widgetId:guid}")]
    public IActionResult SpoofableAsync([FromBody] UnguardedCommand command)
    {
        return Ok(command.WidgetId);
    }
}
