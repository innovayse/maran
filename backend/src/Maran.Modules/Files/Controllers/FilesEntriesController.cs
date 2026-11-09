using Maran.Modules.Files.Commands.ChangeFilePermissions;
using Maran.Modules.Files.Commands.CopyFileEntries;
using Maran.Modules.Files.Commands.CreateFileEntry;
using Maran.Modules.Files.Commands.DeleteFileEntries;
using Maran.Modules.Files.Commands.MoveFileEntries;
using Maran.Modules.Files.Commands.RenameFileEntry;
using Maran.Modules.Files.Common;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// HTTP surface for changing an account's files: create, rename, move, copy, change permissions and delete for
/// good. Thin by design (rules/csharp.md "Controller shape is fixed"): binds the request, dispatches through
/// Wolverine, translates the <see cref="Result{T}"/>. No business logic, no data access.
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
public sealed class FilesEntriesController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FilesEntriesController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Creates an empty file or a folder and returns the new entry. A name already taken answers 409.
    /// </summary>
    /// <param name="command">The owning account, the new path and whether it is a file or a folder.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("entries")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateFileEntryCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileEntryDto>>(command, cancellationToken));
    }

    /// <summary>Renames an entry inside its folder and returns it. A name already taken answers 409.</summary>
    /// <param name="command">The owning account, the entry and its new name.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("rename")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameAsync(
        [FromBody] RenameFileEntryCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileEntryDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Moves each named entry into one folder. Every item is tried; a failure of one is reported on that item.
    /// </summary>
    /// <param name="command">The owning account, the entries and the destination folder.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("move")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveAsync(
        [FromBody] MoveFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileBatchResultDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Copies each named entry into one folder. Every item is tried; a failure of one is reported on that item.
    /// </summary>
    /// <param name="command">The owning account, the entries and the destination folder.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("copy")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyAsync(
        [FromBody] CopyFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileBatchResultDto>>(command, cancellationToken));
    }

    /// <summary>Changes the permission bits of one entry, and of everything inside a folder when asked to.</summary>
    /// <param name="command">The owning account, the entry, the octal mode and the recursive flag.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("permissions")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePermissionsAsync(
        [FromBody] ChangeFilePermissionsCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileBatchResultDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Removes each named entry for good, bypassing the trash. The request must say it is confirmed; every item is
    /// tried.
    /// </summary>
    /// <param name="command">The owning account, the entries and the confirmation.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("delete")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        [FromBody] DeleteFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileBatchResultDto>>(command, cancellationToken));
    }
}
