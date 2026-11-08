using Maran.Modules.Files.Commands.EmptyTrash;
using Maran.Modules.Files.Commands.RestoreTrashEntries;
using Maran.Modules.Files.Commands.TrashFileEntries;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Queries.ListTrashEntries;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// HTTP surface for an account's trash: moving entries into it, listing it, restoring from it and emptying it. Thin
/// by design (rules/csharp.md "Controller shape is fixed"): binds the request, dispatches through Wolverine,
/// translates the <see cref="Result{T}"/>. No business logic, no data access.
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
public sealed class FilesTrashController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    public FilesTrashController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>
    /// Moves each named entry into the trash, from which it can be restored until the retention ends. Every item is
    /// tried.
    /// </summary>
    /// <param name="command">The owning account and the entries.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("trash")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TrashAsync(
        [FromBody] TrashFileEntriesCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileBatchResultDto>>(command, cancellationToken));
    }

    /// <summary>Lists one page of the account's trash. Another customer's account answers 404, not 403.</summary>
    /// <param name="query">The account, and the paging, search and sort, from the query string.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("trash")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<TrashEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrashAsync(
        [FromQuery] ListTrashEntriesQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<TrashEntryDto>>>(query, cancellationToken));
    }

    /// <summary>
    /// Restores each named trash entry to where it was, without replacing anything. Every item is tried.
    /// </summary>
    /// <param name="command">The owning account and the trash ids.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPost("trash/restore")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreAsync(
        [FromBody] RestoreTrashEntriesCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileBatchResultDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Removes everything in the account's trash for good. Another customer's account answers 404, not 403.
    /// </summary>
    /// <param name="accountId">The account whose trash to empty.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpDelete("trash")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(EmptiedTrashDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmptyTrashAsync(
        [FromQuery] Guid accountId,
        CancellationToken cancellationToken)
    {
        var command = new EmptyTrashCommand(accountId, ClientIpAddress, CallerUserAgent);

        return ToActionResult(await _bus.InvokeAsync<Result<EmptiedTrashDto>>(command, cancellationToken));
    }
}
