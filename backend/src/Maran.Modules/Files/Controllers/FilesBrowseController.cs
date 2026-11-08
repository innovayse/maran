using Maran.Modules.Files.Common;
using Maran.Modules.Files.Queries.GetFileInfo;
using Maran.Modules.Files.Queries.ListFileEntries;
using Maran.Modules.Files.Queries.ReadFileText;
using Maran.Modules.Files.Queries.SearchFileEntries;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// HTTP surface for looking at an account's files: the entries of a folder, a name search, the
/// properties of one entry and the text of a file to be edited. Thin by design (rules/csharp.md
/// "Controller shape is fixed"): binds the request, dispatches through Wolverine, translates the
/// <see cref="Result{T}"/>. No business logic, no data access.
/// </summary>
/// <remarks>
/// <para>
/// Open to any signed-in caller on an account whose plan includes the file manager and, for a
/// sub-user, whose owner granted it. What they can SEE is not decided here: every route names an account
/// by row id and the handler admits it through the module's gate, which asks <c>IAccountDirectory</c>, so
/// another customer's account answers 404 and never 403 (spec §8, rules/testing.md item 3).
/// </para>
/// <para>
/// Listing, searching and properties are read-only and an operator viewing as a customer may use them.
/// Reading a file's CONTENT is not "looking": it copies a customer's data out, so a view-as session is
/// refused it, and an administrator who needs the file uses the administrator's own session.
/// </para>
/// </remarks>
[Route("api/v1/files")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Files")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Files)]
[RequiresAccountFeature(AccountFeature.Files)]
public sealed class FilesBrowseController : BaseApiController
{
    /// <summary>The message bus queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the controller with the caller identity and the message bus.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus queries are dispatched through.</param>
    public FilesBrowseController(ICurrentUser currentUser, IMessageBus bus)
        : base(currentUser)
    {
        _bus = bus;
    }

    /// <summary>One server page of the entries directly inside a folder. Another customer's account answers 404, not 403.</summary>
    /// <param name="query">The account, the folder, whether hidden entries are listed, paging, search and sort.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("entries")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(PagedResult<FileEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetEntriesAsync(
        [FromQuery] ListFileEntriesQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<PagedResult<FileEntryDto>>>(query, cancellationToken));
    }

    /// <summary>The properties of one entry. Another customer's account answers 404, not 403.</summary>
    /// <param name="query">The account and the entry's path.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("entries/info")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(FileInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetInfoAsync(
        [FromQuery] GetFileInfoQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileInfoDto>>(query, cancellationToken));
    }

    /// <summary>
    /// One server page of the entries below a folder, at any depth, whose name contains the search text. The
    /// answer says when the agent stopped looking early. Another customer's account answers 404, not 403.
    /// </summary>
    /// <param name="query">The account, the folder to search from, the text, paging and sort.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("search")]
    [ViewAsAccess(ViewAsAccess.Read)]
    [ProducesResponseType(typeof(FileSearchPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] SearchFileEntriesQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileSearchPageDto>>(query, cancellationToken));
    }

    /// <summary>
    /// Reads a file as text for the editor, or refuses a file that is too large, binary or not UTF-8.
    /// Another customer's account answers 404, not 403.
    /// </summary>
    /// <param name="query">The account and the file's path.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpGet("content")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileTextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetContentAsync(
        [FromQuery] ReadFileTextQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileTextDto>>(query, cancellationToken));
    }
}
