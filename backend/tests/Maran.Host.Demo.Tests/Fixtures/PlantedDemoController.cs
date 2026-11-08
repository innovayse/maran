using Microsoft.AspNetCore.Mvc;

namespace Maran.Host.Demo.Tests.Fixtures;

/// <summary>
/// Never routed. Its methods give the guard tests real <see cref="System.Reflection.MethodInfo"/>s to classify.
/// </summary>
public sealed class PlantedDemoController : ControllerBase
{
    /// <summary>A mutation the planted table classifies as refused.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("refused")]
    public IActionResult RefusedAsync()
    {
        return Ok();
    }

    /// <summary>A mutation the planted table allows unless its <c>id</c> is protected.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpDelete("{id}")]
    public IActionResult DeleteAsync()
    {
        return Ok();
    }

    /// <summary>A mutation the planted table allows and does not count.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("open")]
    public IActionResult OpenAsync()
    {
        return Ok();
    }

    /// <summary>A mutation the planted table allows against the sign-in allowances.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("sign-in")]
    public IActionResult SignInAsync()
    {
        return Ok();
    }

    /// <summary>A mutation the planted table allows and counts.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("counted")]
    public IActionResult CountedAsync()
    {
        return Ok();
    }

    /// <summary>A mutation the planted table does not name at all.</summary>
    /// <returns>Nothing meaningful; never executed.</returns>
    [HttpPost("unnamed")]
    public IActionResult UnnamedAsync()
    {
        return Ok();
    }
}
