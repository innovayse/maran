using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted cases of the demo free-text depth census: a mutation whose body nests text past what the demo's walk
/// reads, one whose body contains itself, a shallow one, a read and a refused one, which the census must leave alone.
/// </summary>
/// <remarks>
/// Never composed by the panel. It exists so the census has something real to refuse; without it the walk
/// is satisfied by an empty answer (rules/README.md "Mechanical enforcement").
/// </remarks>
[Route("architecture-tests/demo-text-planted")]
public sealed class DemoTextPlantedController : ControllerBase
{
    /// <summary>A body with text nine lists down.</summary>
    /// <param name="body">Nine nested lists of text.</param>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("deep")]
    public IActionResult DeepAsync(
        [FromBody] List<List<List<List<List<List<List<List<List<string>>>>>>>>> body)
    {
        return Ok(body);
    }

    /// <summary>A body that contains itself.</summary>
    /// <param name="body">A node with a child node.</param>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("recursive")]
    public IActionResult RecursiveAsync([FromBody] DemoTextNode body)
    {
        return Ok(body);
    }

    /// <summary>A body well within the cap, with a name and a cancellation token.</summary>
    /// <param name="body">Two nested lists of text.</param>
    /// <param name="name">A route text.</param>
    /// <param name="cancellationToken">Not a text.</param>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("shallow/{name}")]
    public IActionResult ShallowAsync(
        [FromBody] List<List<string>> body,
        string name,
        CancellationToken cancellationToken)
    {
        return Ok(new { body, name, cancellationToken });
    }

    /// <summary>A read with a deep query: reads are never screened.</summary>
    /// <param name="query">Nine nested lists of text.</param>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpGet("read")]
    public IActionResult ReadAsync(List<List<List<List<List<List<List<List<List<string>>>>>>>>> query)
    {
        return Ok(query);
    }

    /// <summary>A refused mutation with a deep body: the table refuses it, so no text reaches the walk.</summary>
    /// <param name="body">Nine nested lists of text.</param>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("refused")]
    public IActionResult RefusedAsync(
        [FromBody] List<List<List<List<List<List<List<List<List<string>>>>>>>>> body)
    {
        return Ok(body);
    }
}
