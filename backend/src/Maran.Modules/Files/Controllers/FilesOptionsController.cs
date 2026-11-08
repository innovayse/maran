using Maran.Modules.Files.Common;
using Maran.Modules.Files.Queries.GetFilesOptions;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// HTTP surface for the limits and choices the file manager's screens are drawn from. Thin by design
/// (rules/csharp.md "Controller shape is fixed"): dispatches through Wolverine and translates the
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Open to any signed-in caller on an account that has the feature, because the numbers name no account
/// and no row: they are the same for everyone, which is why this controller has no tenant dimension and
/// no IDOR to answer. The interface holds no limit of its own (rules/architecture.md "The backend owns the
/// data, the SPA renders it"); every route enforces the limit again.
/// </remarks>
[Route("api/v1/files/options")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Files")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Files)]
[RequiresAccountFeature(AccountFeature.Files)]
public sealed class FilesOptionsController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public FilesOptionsController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>Reads the transfer, edit and archive limits, the trash retention and the archive formats.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The limits and choices.</returns>
    [HttpGet]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(FilesOptionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<Result<FilesOptionsDto>>(new GetFilesOptionsQuery(), cancellationToken);
        return ToActionResult(result);
    }
}
