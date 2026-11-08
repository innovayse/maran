using Maran.Modules.Files.Commands.CreateFileArchive;
using Maran.Modules.Files.Commands.ExtractFileArchive;
using Maran.Modules.Files.Common;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// HTTP surface for packing entries into an archive and unpacking one. Thin by design (rules/csharp.md "Controller
/// shape is fixed"): binds the request, dispatches through Wolverine, translates the <see cref="Result{T}"/>. No
/// business logic, no data access.
/// </summary>
/// <remarks>
/// Open to any signed-in caller whose plan includes the file manager, because the files belong to an
/// account and the account's holder manages them. What a caller can reach is not decided here: every route
/// names an account by row id and the handler resolves it through the tenant-filtered account directory, so
/// another customer's account answers 404 and never 403, which would confirm it exists
/// (rules/security.md item 6). Every mutation is refused to an operator viewing as a customer: changing a
/// customer's files is not looking at them.
/// </remarks>
[Route("api/v1/files")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Files")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Files)]
[RequiresAccountFeature(AccountFeature.Files)]
public sealed class FilesArchivesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FilesArchivesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Packs the named entries into one archive file and returns its size. A folder is downloaded by archiving it and
    /// downloading the archive.
    /// </summary>
    /// <param name="command">The owning account, the entries, the archive path and its format.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("archive")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileArchivedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateFileArchiveCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileArchivedDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Unpacks an archive into a folder. An archive holding a member the agent will not write is refused as a whole.
    /// </summary>
    /// <param name="command">The owning account, the archive and the destination folder.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("extract")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileExtractedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExtractAsync(
        [FromBody] ExtractFileArchiveCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileExtractedDto>>(command, cancellationToken));
    }
}
