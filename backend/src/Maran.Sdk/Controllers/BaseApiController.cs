using Maran.Sdk.Extensions;
using Maran.SharedKernel.Constants;

namespace Maran.Sdk.Controllers;

/// <summary>
/// The base every module controller inherits — never <see cref="ControllerBase"/> directly
/// (rules/csharp.md "Cross-cutting infrastructure"). Fixes the route convention, exposes the
/// authenticated caller and the request's correlation id, and gives derived controllers the
/// <see cref="Result{T}"/>-to-<see cref="IActionResult"/> translation via <see cref="ApiResultExtensions"/>
/// so no module re-implements it.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>Creates the controller with the caller identity every derived controller needs.</summary>
    /// <param name="currentUser">
    /// The authenticated principal of the current request. No implementation is registered until
    /// authentication ships (Plan 2); no module controller exists yet to exercise this either.
    /// </param>
    protected BaseApiController(ICurrentUser currentUser)
    {
        CurrentUser = currentUser;
    }

    /// <summary>The authenticated principal of the current request.</summary>
    protected ICurrentUser CurrentUser { get; }

    /// <summary>The correlation id assigned to this request by <c>CorrelationIdMiddleware</c>, or <c>null</c> outside a request.</summary>
    protected string? CorrelationId =>
        HttpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var value) ? value as string : null;

    /// <summary>Translates a query/read result into 200 OK or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    protected IActionResult ToActionResult<T>(Result<T> result) => result.ToActionResult(HttpContext);

    /// <summary>Translates a create result into 201 Created (with <paramref name="location"/>) or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    /// <param name="location">The URI of the created resource, used as the 201 response's <c>Location</c> header.</param>
    protected IActionResult ToCreatedActionResult<T>(Result<T> result, string location) =>
        result.ToCreatedActionResult(HttpContext, location);
}
